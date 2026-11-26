namespace SafeNlToSql.Application;

public interface ISqlGenerator
{
    /// <summary>
    /// Returns T-SQL text for the question, or the literal NO_QUERY when it cannot be answered.
    /// The text is untrusted: nothing may run it unless SqlGuard approves it.
    /// </summary>
    Task<string> GenerateAsync(string question, CancellationToken ct);
}
