namespace SafeNlToSql.Application;

/// <summary>
/// The outcome of SqlGuard. It is allowed exactly when it carries an <see cref="ApprovedSql"/>, and only
/// SqlGuard (inside this assembly) can create one; outside callers can only build a rejection.
/// </summary>
public sealed record GuardResult
{
    private GuardResult(ApprovedSql? approved, string? reason)
    {
        Approved = approved;
        Reason = reason;
    }

    /// <summary>The approved SQL, or null for a rejection.</summary>
    public ApprovedSql? Approved { get; }

    public string? Reason { get; }

    public bool IsAllowed => Approved is not null;

    internal static GuardResult Allow(string sql) => new(new ApprovedSql(sql), null);

    public static GuardResult Reject(string reason) => new(null, reason);
}
