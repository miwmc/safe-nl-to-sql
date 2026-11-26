using SafeNlToSql.Application;

namespace SafeNlToSql.Api;

/// <summary>Stands in until a real generator is registered, so the app starts without Azure. /search returns 503.</summary>
internal sealed class NotConfiguredSqlGenerator : ISqlGenerator
{
    public Task<string> GenerateAsync(string question, CancellationToken ct) =>
        throw new InvalidOperationException("No SQL generator is configured.");
}
