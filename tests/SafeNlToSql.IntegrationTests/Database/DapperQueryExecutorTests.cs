using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SafeNlToSql.Application;
using SafeNlToSql.Infrastructure;

namespace SafeNlToSql.IntegrationTests.Database;

/// <summary>All SQL goes through SqlGuard first: the executor only accepts what the guard approved.</summary>
[Collection("Database")]
public class DapperQueryExecutorTests(DatabaseFixture db)
{
    private DapperQueryExecutor CreateExecutor(
        int timeoutSeconds = 5, string? connectionString = null, ILogger<DapperQueryExecutor>? logger = null) =>
        new(connectionString ?? db.ReaderConnectionString,
            Options.Create(new DatabaseOptions { CommandTimeoutSeconds = timeoutSeconds }),
            logger ?? NullLogger<DapperQueryExecutor>.Instance);

    private static ApprovedSql Approve(string sql)
    {
        var result = new SqlGuard().Validate(sql);
        Assert.True(result.IsAllowed, $"SqlGuard rejected the test SQL: {result.Reason}");
        return result.Approved!;
    }

    [Fact]
    public async Task Executes_approved_select_and_returns_rows()
    {
        var result = await CreateExecutor().ExecuteAsync(
            Approve("SELECT TOP 5 CertificateNumber, EquipmentName FROM dbo.Certificates ORDER BY CertificateNumber"),
            CancellationToken.None);

        Assert.Null(result.Reason);
        Assert.Equal(5, result.Rows.Count);
        Assert.Equal("CAL-2025-0001", result.Rows[0]["CertificateNumber"]);
    }

    [Fact]
    public async Task Returns_only_the_selected_columns()
    {
        var result = await CreateExecutor().ExecuteAsync(
            Approve("SELECT TOP 3 EquipmentName, IsCompliant FROM dbo.Certificates"),
            CancellationToken.None);

        Assert.All(result.Rows, row => Assert.Equal(["EquipmentName", "IsCompliant"], row.Keys.Order().ToList()));
    }

    [Fact]
    public async Task Executor_filtered_count_star_returns_rows()
    {
        var expected = await SqlHelper.ScalarAsync<int>(db.SaConnectionString,
            "SELECT COUNT(*) FROM dbo.Certificates WHERE IsCompliant = 0");

        var result = await CreateExecutor().ExecuteAsync(
            Approve("SELECT TOP 1 COUNT(*) AS NonCompliant FROM dbo.Certificates WHERE IsCompliant = 0"),
            CancellationToken.None);

        Assert.Null(result.Reason);
        Assert.Equal(expected, Assert.Single(result.Rows)["NonCompliant"]);
    }

    [Fact]
    public async Task Executor_bare_count_star_returns_QueryNotPermitted()
    {
        // The guard approves this (COUNT(*) is allowed), the database denies it with 230: no allowed column is referenced.
        var approved = Approve("SELECT TOP 1 COUNT(*) AS Total FROM dbo.Certificates");

        var result = await CreateExecutor().ExecuteAsync(approved, CancellationToken.None);

        Assert.Equal(QueryExecutionReasons.QueryNotPermitted, result.Reason);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Executor_object_permission_denied_229_returns_QueryNotPermitted()
    {
        // The guard approves this query, but we revoke the reader's SELECT on dbo.Certificates so the database refuses it with 229.
        // The finally block runs the db scripts again, so the reader's permissions match 03_reader.sql for the other tests.
        await SqlHelper.ExecuteAsync(db.SaConnectionString, "REVOKE SELECT ON dbo.Certificates FROM nl_search_reader;");
        try
        {
            const string sql = "SELECT TOP 1 EquipmentName FROM dbo.Certificates";

            // First prove that the setup really produces 229, so this test cannot pass without a denial.
            await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString, sql, 229);

            var result = await CreateExecutor().ExecuteAsync(Approve(sql), CancellationToken.None);

            Assert.Equal(QueryExecutionReasons.QueryNotPermitted, result.Reason);
            Assert.Empty(result.Rows);
        }
        finally
        {
            await db.RunScriptsAsync();
        }
    }

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentType, COUNT(CertificateNumber) FROM dbo.Certificates", 8120)]
    [InlineData("SELECT TOP 10 CertificateNumber FROM dbo.Certificates WHERE CalibrationDate > 'last month'", 241)]
    [InlineData("SELECT TOP 10 CertificateNumber FROM dbo.Certificates WHERE CalibrationDate > CAST('1000-01-01' AS datetime)", 242)]
    [InlineData("SELECT TOP 10 CertificateNumber FROM dbo.Certificates WHERE EquipmentName = 1", 245)]
    [InlineData("SELECT TOP 10 CertificateNumber FROM dbo.Certificates WHERE EquipmentName = 1.5", 8114)]
    [InlineData("SELECT TOP 10 CertificateNumber, CAST(1000 AS decimal(2,0)) AS Small FROM dbo.Certificates", 8115)]
    [InlineData("SELECT TOP 10 CertificateNumber, 1 / 0 AS Ratio FROM dbo.Certificates", 8134)]
    public async Task Query_error_returns_QueryFailed_and_logs_the_exception(string sql, int expectedNumber)
    {
        // First prove that the database really raises this number, so the test cannot pass for another error.
        await SqlHelper.AssertSqlErrorAsync(db.ReaderConnectionString, sql, expectedNumber);
        var logger = new RecordingLogger();

        var result = await CreateExecutor(logger: logger).ExecuteAsync(Approve(sql), CancellationToken.None);

        // The fixed reason, so none of the database message is returned.
        Assert.Equal(QueryExecutionReasons.QueryFailed, result.Reason);
        Assert.Empty(result.Rows);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(expectedNumber, Assert.IsType<SqlException>(entry.Exception).Number);
    }

    [Fact]
    public async Task Query_error_not_on_the_list_throws()
    {
        // 8117: MAX does not accept a bit column. It is the query's own error, but it is not on the list.
        const string sql = "SELECT TOP 1 MAX(IsCompliant) AS Highest FROM dbo.Certificates";
        await SqlHelper.AssertSqlErrorAsync(db.ReaderConnectionString, sql, 8117);
        var approved = Approve(sql);

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => CreateExecutor().ExecuteAsync(approved, CancellationToken.None));

        Assert.Equal(8117, exception.Number);
    }

    [Fact]
    public async Task Wrong_password_throws()
    {
        var connectionString = new SqlConnectionStringBuilder(db.ReaderConnectionString)
        {
            Password = "Wrong-password-1!",
        }.ConnectionString;

        await AssertConnectionFailureThrowsAsync(connectionString, 18456);
    }

    [Fact]
    public async Task Missing_database_throws()
    {
        var connectionString = new SqlConnectionStringBuilder(db.ReaderConnectionString)
        {
            InitialCatalog = "NoSuchDatabase",
        }.ConnectionString;

        await AssertConnectionFailureThrowsAsync(connectionString, 4060);
    }

    private async Task AssertConnectionFailureThrowsAsync(string connectionString, int expectedNumber)
    {
        var approved = Approve("SELECT TOP 1 EquipmentName FROM dbo.Certificates");

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => CreateExecutor(connectionString: connectionString).ExecuteAsync(approved, CancellationToken.None));

        Assert.Equal(expectedNumber, exception.Number);
    }

    [Fact]
    public async Task Double_quoted_name_is_treated_as_identifier()
    {
        // With QUOTED_IDENTIFIER OFF this would return the literal string "EquipmentType" five times.
        var result = await CreateExecutor().ExecuteAsync(
            Approve("""SELECT TOP 5 "EquipmentType" AS T FROM dbo.Certificates"""),
            CancellationToken.None);

        string[] realTypes = ["Fridge", "Microwave", "Microphone", "Speaker"];
        Assert.Equal(5, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Contains((string)row["T"]!, realTypes));
    }

    [Fact]
    public async Task Unnamed_columns_are_named_by_position()
    {
        var result = await CreateExecutor().ExecuteAsync(
            Approve("SELECT TOP 1 COUNT(Metric), MIN(CalibrationDate) FROM dbo.Certificates"),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(["Column1", "Column2"], row.Keys.ToList());
        Assert.Equal(50, row["Column1"]);
        Assert.IsType<DateTime>(row["Column2"]);
    }

    [Fact]
    public async Task Repeated_column_names_get_a_suffix_and_keep_their_values()
    {
        var result = await CreateExecutor().ExecuteAsync(
            Approve("SELECT TOP 5 EquipmentName, EquipmentName FROM dbo.Certificates"),
            CancellationToken.None);

        Assert.Equal(5, result.Rows.Count);
        Assert.All(result.Rows, row =>
        {
            Assert.Equal(["EquipmentName", "EquipmentName_2"], row.Keys.ToList());
            Assert.NotNull(row["EquipmentName"]);
            Assert.Equal(row["EquipmentName"], row["EquipmentName_2"]);
        });
    }

    [Fact]
    public async Task Generated_names_never_overwrite_a_real_column()
    {
        // The unnamed first column would be "Column1", which the second column really is called.
        var result = await CreateExecutor().ExecuteAsync(
            Approve("SELECT TOP 1 COUNT(Metric), MIN(CalibrationDate) AS Column1 FROM dbo.Certificates"),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(2, row.Count);
        Assert.IsType<DateTime>(row["Column1"]);
        Assert.Contains(50, row.Values);
    }

    [Fact]
    public async Task Blocked_query_returns_QueryTimeout()
    {
        // The guard would reject anything expensive, so make an approved query slow by blocking it:
        // another session holds an exclusive table lock, and the reader's SELECT waits for it.
        await using var blocker = await TableLock.AcquireAsync(db.SaConnectionString);

        var stopwatch = Stopwatch.StartNew();
        var result = await CreateExecutor(timeoutSeconds: 1).ExecuteAsync(
            Approve("SELECT TOP 10 EquipmentName FROM dbo.Certificates"),
            CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(QueryExecutionReasons.QueryTimeout, result.Reason);
        Assert.Empty(result.Rows);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Cancellation_throws_OperationCanceledException()
    {
        await using var blocker = await TableLock.AcquireAsync(db.SaConnectionString);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // A long command timeout, so this is a cancellation and not a timeout.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateExecutor(timeoutSeconds: 30).ExecuteAsync(
            Approve("SELECT TOP 10 EquipmentName FROM dbo.Certificates"),
            cts.Token));
    }

    [Fact]
    public void Default_command_timeout_is_5_seconds()
    {
        Assert.Equal(5, new DatabaseOptions().CommandTimeoutSeconds);
    }

    [Fact]
    public void AddInfrastructure_registers_the_executor()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Reader"] = db.ReaderConnectionString })
            .Build();

        using var provider = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();

        Assert.IsType<DapperQueryExecutor>(provider.GetRequiredService<IQueryExecutor>());
        Assert.Equal(5, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.CommandTimeoutSeconds);
    }

    [Fact]
    public void AddInfrastructure_without_reader_connection_string_throws()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(configuration));
    }

    private sealed class RecordingLogger : ILogger<DapperQueryExecutor>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }
}
