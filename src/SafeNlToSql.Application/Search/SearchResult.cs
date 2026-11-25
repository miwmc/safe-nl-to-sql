namespace SafeNlToSql.Application;

public enum SearchOutcome
{
    Answered,
    Rejected,
    GeneratorUnavailable,
}

/// <summary>
/// The outcome of a search. Only the factories can build one, so <see cref="Sql"/> is set only when the
/// query ran and was answered, and every other outcome has no SQL and no rows.
/// </summary>
public sealed record SearchResult
{
    private SearchResult(
        SearchOutcome outcome, string? sql, string? reason, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        Outcome = outcome;
        Sql = sql;
        Reason = reason;
        Rows = rows;
    }

    public SearchOutcome Outcome { get; }

    public string? Sql { get; }

    public string? Reason { get; }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; }

    public static SearchResult Answered(ApprovedSql sql, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        new(SearchOutcome.Answered, sql.Sql, null, rows);

    public static SearchResult Rejected(string reason) => new(SearchOutcome.Rejected, null, reason, []);

    public static SearchResult GeneratorUnavailable() => new(SearchOutcome.GeneratorUnavailable, null, null, []);
}
