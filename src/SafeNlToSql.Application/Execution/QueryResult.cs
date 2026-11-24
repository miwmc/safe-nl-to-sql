namespace SafeNlToSql.Application;

public sealed record QueryResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, string? Reason)
{
    public static QueryResult Success(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => new(rows, null);

    /// <summary>An empty result with a reason; used for failures the executor handles itself, such as a timeout.</summary>
    public static QueryResult Failed(string reason) => new([], reason);
}
