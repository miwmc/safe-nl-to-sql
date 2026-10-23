namespace SafeNlToSql.Application;

public sealed record GuardResult(bool IsAllowed, string? Reason)
{
    public static GuardResult Allow() => new(true, null);

    public static GuardResult Reject(string reason) => new(false, reason);
}
