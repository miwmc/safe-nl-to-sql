using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SafeNlToSql.Application;
using SafeNlToSql.IntegrationTests.Database;

namespace SafeNlToSql.IntegrationTests.Search;

[Collection("Database")]
public class SearchEndpointTests(DatabaseFixture db)
{
    private const string Question = "show me all non-compliant microwaves calibrated in the last 30 days";

    private const string MicrowaveFilter =
        "EquipmentType = 'Microwave' AND IsCompliant = 0 AND CalibrationDate >= DATEADD(day, -30, GETDATE())";

    private const string AnsweredSql =
        "SELECT TOP 100 CertificateNumber, EquipmentName, EquipmentType, CalibrationDate, IsCompliant " +
        "FROM dbo.Certificates WHERE " + MicrowaveFilter;

    private const string ClientNameSql = "SELECT TOP 5 ClientName FROM dbo.Certificates";

    private const string Sentinel = "LEAK-SENTINEL SELECT ClientName";

    [Fact]
    public async Task Answered_question_returns_rows_with_camelCase_keys()
    {
        var expectedCount = await SqlHelper.ScalarAsync<int>(
            db.SaConnectionString, "SELECT COUNT(*) FROM dbo.Certificates WHERE " + MicrowaveFilter);
        Assert.True(expectedCount > 0, "The seed data has no matching row, so this test would prove nothing.");

        using var factory = new SearchApiFactory(db);
        var (status, body) = await AskAsync(factory, AnsweredSql);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(["reason", "rejected", "rows", "sql"], PropertyNames(root));
        Assert.False(root.GetProperty("rejected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("reason").ValueKind);
        Assert.Equal(AnsweredSql, root.GetProperty("sql").GetString());
        Assert.Equal(expectedCount, root.GetProperty("rows").GetArrayLength());
        Assert.All(root.GetProperty("rows").EnumerateArray(), row => Assert.Equal(
            ["calibrationDate", "certificateNumber", "equipmentName", "equipmentType", "isCompliant"],
            PropertyNames(row)));
    }

    [Fact]
    public async Task Guard_rejects_query_the_database_would_allow()
    {
        const string sql = "SELECT CertificateNumber FROM dbo.Certificates";

        // The reader may run this, so only the guard can be what rejects it.
        await SqlHelper.ExecuteAsync(db.ReaderConnectionString, sql);

        using var factory = new SearchApiFactory(db);
        var (status, body) = await AskAsync(factory, sql);

        AssertRejected(status, body, SqlGuardReasons.TopMissing);
    }

    [Fact]
    public async Task Guard_rejects_ClientName_before_database()
    {
        using var factory = new SearchApiFactory(db);
        var (status, body) = await AskAsync(factory, ClientNameSql);

        // The database would answer with QueryNotPermitted, so this reason shows the guard stopped it first.
        AssertRejected(status, body, SqlGuardReasons.ColumnNotAllowed);
    }

    [Fact]
    public async Task Database_denies_query_the_guard_approved()
    {
        const string sql = "SELECT TOP 1 COUNT(*) FROM dbo.Certificates";
        Assert.True(new SqlGuard().Validate(sql).IsAllowed);

        using var factory = new SearchApiFactory(db);
        var (status, body) = await AskAsync(factory, sql);

        AssertRejected(status, body, QueryExecutionReasons.QueryNotPermitted);
    }

    [Fact]
    public async Task No_query_returns_rejected_with_reason()
    {
        using var factory = new SearchApiFactory(db);
        var (status, body) = await AskAsync(factory, "  NO_QUERY\n");

        AssertRejected(status, body, SearchReasons.NoQuery);
        Assert.DoesNotContain("NO_QUERY", body);
    }

    [Fact]
    public async Task Blocked_query_returns_rejected_QueryTimeout()
    {
        using var factory = new SearchApiFactory(db);
        using var shortTimeout = factory.WithWebHostBuilder(
            builder => builder.UseSetting("Database:CommandTimeoutSeconds", "1"));
        factory.Generator.Add(Question, "SELECT TOP 10 EquipmentName FROM dbo.Certificates");
        using var client = shortTimeout.CreateClient();
        await using var blocker = await TableLock.AcquireAsync(db.SaConnectionString);

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.PostAsJsonAsync("/search", new { question = Question });
        stopwatch.Stop();

        AssertRejected(response.StatusCode, await response.Content.ReadAsStringAsync(), QueryExecutionReasons.QueryTimeout);

        // The default timeout is 5 seconds, so finishing sooner shows the configured 1 second was used.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("""{"question":""}""")]
    [InlineData("""{"question":"   "}""")]
    [InlineData("""{"question":null}""")]
    [InlineData("{}")]
    public async Task Invalid_question_returns_400_without_calling_generator(string json)
    {
        using var factory = new SearchApiFactory(db);

        await AssertBadRequestAsync(factory, json);
    }

    [Fact]
    public async Task Question_of_501_characters_returns_400_without_calling_generator()
    {
        using var factory = new SearchApiFactory(db);

        await AssertBadRequestAsync(factory, JsonSerializer.Serialize(new { question = new string('a', 501) }));
    }

    [Fact]
    public async Task Question_of_500_characters_is_accepted()
    {
        var question = new string('a', 500);
        using var factory = new SearchApiFactory(db);
        factory.Generator.Add(question, "NO_QUERY");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/search", new { question });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([question], factory.Generator.Questions);
    }

    [Fact]
    public async Task Default_generator_returns_503()
    {
        using var factory = new SearchApiFactory(db, useFakeGenerator: false);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/search", new { question = Question });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("configured", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Generator_failure_returns_503_without_details()
    {
        using var factory = new SearchApiFactory(db);
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISqlGenerator>();
            services.AddSingleton<ISqlGenerator>(new ThrowingSqlGenerator());
        }));
        using var client = failing.CreateClient();

        using var response = await client.PostAsJsonAsync("/search", new { question = Question });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoSentinel(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unexpected_executor_error_returns_500_without_details()
    {
        using var factory = new SearchApiFactory(db);
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IQueryExecutor>();
            services.AddSingleton<IQueryExecutor>(new ThrowingQueryExecutor());
        }));
        factory.Generator.Add(Question, AnsweredSql);
        using var client = failing.CreateClient();

        using var response = await client.PostAsJsonAsync("/search", new { question = Question });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertNoSentinel(body);
        Assert.DoesNotContain("dbo.Certificates", body);
    }

    [Theory]
    [InlineData(AnsweredSql)]
    [InlineData(ClientNameSql)]
    public async Task Response_never_contains_client_names(string sql)
    {
        var clientNames = await SqlHelper.FirstColumnAsync(
            db.SaConnectionString, "SELECT DISTINCT ClientName FROM dbo.Certificates");
        Assert.NotEmpty(clientNames);

        using var factory = new SearchApiFactory(db);
        var (status, body) = await AskAsync(factory, sql);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("\"clientName\"", body, StringComparison.OrdinalIgnoreCase);
        // Compare in the encoded form too: the serializer escapes characters such as & and '.
        Assert.All(clientNames, name =>
        {
            Assert.DoesNotContain(name, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(JsonEncodedText.Encode(name).ToString(), body, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static async Task<(HttpStatusCode Status, string Body)> AskAsync(SearchApiFactory factory, string generatedSql)
    {
        factory.Generator.Add(Question, generatedSql);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/search", new { question = Question });
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A rejection is 200 with a reason, null sql, no rows, and none of the generated SQL.</summary>
    private static void AssertRejected(HttpStatusCode status, string body, string expectedReason)
    {
        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(["reason", "rejected", "rows", "sql"], PropertyNames(root));
        Assert.True(root.GetProperty("rejected").GetBoolean());
        Assert.Equal(expectedReason, root.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sql").ValueKind);
        Assert.Equal(0, root.GetProperty("rows").GetArrayLength());

        // None of the reasons these tests expect mentions the table, so this would be echoed SQL.
        Assert.DoesNotContain("FROM dbo.Certificates", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertBadRequestAsync(SearchApiFactory factory, string json)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/search", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Generator.Questions);
    }

    private static void AssertNoSentinel(string body)
    {
        Assert.DoesNotContain("LEAK-SENTINEL", body);
        Assert.DoesNotContain("ClientName", body);
        Assert.DoesNotContain("Exception", body);
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private sealed class ThrowingSqlGenerator : ISqlGenerator
    {
        public Task<string> GenerateAsync(string question, CancellationToken ct) =>
            throw new InvalidOperationException(Sentinel);
    }

    private sealed class ThrowingQueryExecutor : IQueryExecutor
    {
        public Task<QueryResult> ExecuteAsync(ApprovedSql sql, CancellationToken ct) =>
            throw new InvalidOperationException(Sentinel);
    }
}
