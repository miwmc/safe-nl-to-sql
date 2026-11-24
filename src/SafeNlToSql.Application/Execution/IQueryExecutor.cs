namespace SafeNlToSql.Application;

public interface IQueryExecutor
{
    /// <summary>Runs SQL that SqlGuard approved. Unapproved text cannot be passed: the type cannot be built outside Application.</summary>
    Task<QueryResult> ExecuteAsync(ApprovedSql sql, CancellationToken ct);
}
