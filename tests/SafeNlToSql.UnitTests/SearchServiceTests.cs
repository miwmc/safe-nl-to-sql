using Microsoft.Extensions.Logging.Abstractions;
using SafeNlToSql.Application;

namespace SafeNlToSql.UnitTests;

public class SearchServiceTests
{
    private const string Question = "which microwaves failed?";
    private const string ValidSql = "SELECT TOP 5 CertificateNumber FROM dbo.Certificates";

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> SomeRows =
        [new Dictionary<string, object?> { ["CertificateNumber"] = "CAL-TEST-1" }];

    private static SearchService CreateService(ISqlGenerator generator, IQueryExecutor executor) =>
        new(generator, new SqlGuard(), executor, NullLogger<SearchService>.Instance);

    [Fact]
    public async Task Approved_sql_is_executed_and_rows_returned()
    {
        var generator = StubSqlGenerator.Returning(ValidSql);
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        var result = await CreateService(generator, executor).SearchAsync(Question, CancellationToken.None);

        Assert.Equal(SearchOutcome.Answered, result.Outcome);
        Assert.Equal(ValidSql, result.Sql);
        Assert.Null(result.Reason);
        Assert.Same(SomeRows, result.Rows);
        Assert.Equal([Question], generator.Questions);
        Assert.Equal([ValidSql], executor.ExecutedSql);
    }

    [Fact]
    public async Task Generated_text_is_passed_to_guard_and_executor_verbatim()
    {
        const string text = "\r\n  " + ValidSql + "  -- generated\r\n  ";
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        var result = await CreateService(StubSqlGenerator.Returning(text), executor)
            .SearchAsync(Question, CancellationToken.None);

        Assert.Equal(text, result.Sql);
        Assert.Equal([text], executor.ExecutedSql);
    }

    [Theory]
    [InlineData("NO_QUERY")]
    [InlineData("  NO_QUERY  ")]
    [InlineData("NO_QUERY\r\n")]
    public async Task No_query_literal_is_rejected_without_guard_or_executor(string text)
    {
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        var result = await CreateService(StubSqlGenerator.Returning(text), executor)
            .SearchAsync(Question, CancellationToken.None);

        // The guard never produces this reason, so it also shows the text did not go through the guard.
        Assert.Equal(SearchOutcome.Rejected, result.Outcome);
        Assert.Equal(SearchReasons.NoQuery, result.Reason);
        Assert.Empty(executor.ExecutedSql);
    }

    [Theory]
    [InlineData("no_query")]
    [InlineData("NO_QUERY;")]
    [InlineData("`NO_QUERY`")]
    [InlineData("NO_QUERY.")]
    public async Task No_query_lookalikes_go_to_the_guard(string text)
    {
        var expected = new SqlGuard().Validate(text);
        Assert.False(expected.IsAllowed);
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        var result = await CreateService(StubSqlGenerator.Returning(text), executor)
            .SearchAsync(Question, CancellationToken.None);

        Assert.Equal(SearchOutcome.Rejected, result.Outcome);
        Assert.Equal(expected.Reason, result.Reason);
        Assert.NotEqual(SearchReasons.NoQuery, result.Reason);
        Assert.Empty(executor.ExecutedSql);
    }

    [Fact]
    public async Task Guard_rejection_returns_reason_and_does_not_execute()
    {
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        var result = await CreateService(
                StubSqlGenerator.Returning("SELECT TOP 5 ClientName FROM dbo.Certificates"), executor)
            .SearchAsync(Question, CancellationToken.None);

        Assert.Equal(SearchOutcome.Rejected, result.Outcome);
        Assert.Equal(SqlGuardReasons.ColumnNotAllowed, result.Reason);
        Assert.Empty(executor.ExecutedSql);
    }

    [Theory]
    [InlineData("NO_QUERY")]
    [InlineData("DELETE FROM dbo.Certificates")]
    [InlineData(ValidSql)] // approved by the guard, then failed by the executor
    public async Task Rejected_result_has_null_sql_and_empty_rows(string text)
    {
        // A failed result that still carries rows: the service must not pass them on.
        var executor = StubQueryExecutor.Returning(new QueryResult(SomeRows, QueryExecutionReasons.QueryTimeout));

        var result = await CreateService(StubSqlGenerator.Returning(text), executor)
            .SearchAsync(Question, CancellationToken.None);

        Assert.Equal(SearchOutcome.Rejected, result.Outcome);
        Assert.Null(result.Sql);
        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData(QueryExecutionReasons.QueryNotPermitted)]
    [InlineData(QueryExecutionReasons.QueryTimeout)]
    public async Task Executor_failure_maps_to_rejected_with_its_reason(string reason)
    {
        var executor = StubQueryExecutor.Returning(QueryResult.Failed(reason));

        var result = await CreateService(StubSqlGenerator.Returning(ValidSql), executor)
            .SearchAsync(Question, CancellationToken.None);

        Assert.Equal(SearchOutcome.Rejected, result.Outcome);
        Assert.Equal(reason, result.Reason);
        Assert.Equal([ValidSql], executor.ExecutedSql);
    }

    [Fact]
    public async Task Generator_exception_returns_GeneratorUnavailable_without_message()
    {
        var generator = StubSqlGenerator.Throwing(new InvalidOperationException("LEAK-SENTINEL SELECT ClientName"));
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        var result = await CreateService(generator, executor).SearchAsync(Question, CancellationToken.None);

        Assert.Equal(SearchOutcome.GeneratorUnavailable, result.Outcome);
        Assert.Null(result.Reason);
        Assert.Null(result.Sql);
        Assert.Empty(result.Rows);
        Assert.Empty(executor.ExecutedSql);
    }

    [Fact]
    public async Task Generator_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var generator = StubSqlGenerator.Throwing(new OperationCanceledException(cts.Token));
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(generator, executor).SearchAsync(Question, cts.Token));
        Assert.Empty(executor.ExecutedSql);
    }

    [Fact]
    public async Task Generator_own_timeout_returns_GeneratorUnavailable()
    {
        // An HTTP client timeout is also an OperationCanceledException, but the caller did not cancel.
        var generator = StubSqlGenerator.Throwing(new TaskCanceledException());
        var executor = StubQueryExecutor.Returning(QueryResult.Success(SomeRows));

        var result = await CreateService(generator, executor).SearchAsync(Question, CancellationToken.None);

        Assert.Equal(SearchOutcome.GeneratorUnavailable, result.Outcome);
    }

    [Fact]
    public async Task Unexpected_executor_exception_propagates()
    {
        var executor = StubQueryExecutor.Throwing(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(StubSqlGenerator.Returning(ValidSql), executor)
                .SearchAsync(Question, CancellationToken.None));
    }

    private sealed class StubSqlGenerator(Func<string> generate) : ISqlGenerator
    {
        public List<string> Questions { get; } = [];

        public static StubSqlGenerator Returning(string sql) => new(() => sql);

        public static StubSqlGenerator Throwing(Exception exception) => new(() => throw exception);

        public Task<string> GenerateAsync(string question, CancellationToken ct)
        {
            Questions.Add(question);
            return Task.FromResult(generate());
        }
    }

    private sealed class StubQueryExecutor(Func<QueryResult> execute) : IQueryExecutor
    {
        public List<string> ExecutedSql { get; } = [];

        public static StubQueryExecutor Returning(QueryResult result) => new(() => result);

        public static StubQueryExecutor Throwing(Exception exception) => new(() => throw exception);

        public Task<QueryResult> ExecuteAsync(ApprovedSql sql, CancellationToken ct)
        {
            ExecutedSql.Add(sql.Sql);
            return Task.FromResult(execute());
        }
    }
}
