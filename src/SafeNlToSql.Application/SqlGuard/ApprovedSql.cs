namespace SafeNlToSql.Application;

/// <summary>
/// SQL text that SqlGuard has approved. The constructor is internal and Application only exposes
/// its internals to the unit tests, so the Api and Infrastructure can get an instance only from
/// <see cref="SqlGuard.Validate"/>. <see cref="IQueryExecutor"/> accepts nothing else.
/// The text is exactly what was validated, never re-scripted or repaired.
/// </summary>
public sealed class ApprovedSql
{
    internal ApprovedSql(string sql)
    {
        Sql = sql;
    }

    public string Sql { get; }
}
