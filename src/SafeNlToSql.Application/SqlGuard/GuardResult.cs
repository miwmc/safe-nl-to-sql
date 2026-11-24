namespace SafeNlToSql.Application;

public sealed record GuardResult(bool IsAllowed, string? Reason)
{
    /// <summary>The approved SQL when <see cref="IsAllowed"/> came from SqlGuard; null for a rejection.</summary>
    public ApprovedSql? Approved { get; private init; }

    internal static GuardResult Allow(string sql) => new(true, null) { Approved = new ApprovedSql(sql) };

    public static GuardResult Reject(string reason) => new(false, reason);
}
