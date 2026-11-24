namespace SafeNlToSql.Infrastructure;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Command timeout for approved queries. The guard bounds rows and table references but not
    /// execution time, so this is the only bound on how long a query may run.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 5;
}
