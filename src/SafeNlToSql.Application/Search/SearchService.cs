using Microsoft.Extensions.Logging;

namespace SafeNlToSql.Application;

/// <summary>
/// Question in, result out: generate SQL, check it with SqlGuard, and execute only what the guard approved.
/// The generated text is never repaired or rewritten. The caller validates the question first.
/// </summary>
public sealed class SearchService(
    ISqlGenerator generator, SqlGuard guard, IQueryExecutor executor, ILogger<SearchService> logger)
{
    public const int MaxQuestionLength = 500;

    private const string NoQueryLiteral = "NO_QUERY";

    public async Task<SearchResult> SearchAsync(string question, CancellationToken ct)
    {
        string text;
        try
        {
            text = await generator.GenerateAsync(question, ct);
        }
        catch (GeneratorRefusedException ex)
        {
            // Asking again cannot help, so this is a rejection and not a failure. Only the fixed reason is returned.
            logger.LogWarning("The SQL generator refused the question: {Detail}", ex.Message);
            return SearchResult.Rejected(SearchReasons.ContentFiltered);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Logged for the operator only: the message may contain model output, so it never reaches the result.
            // A cancellation the caller did not ask for, such as the generator's own timeout, lands here too.
            logger.LogError(ex, "The SQL generator failed");
            return SearchResult.GeneratorUnavailable();
        }

        // The return type is not nullable, but nothing enforces that at runtime.
        if (text is null)
        {
            logger.LogError("The SQL generator returned null");
            return SearchResult.GeneratorUnavailable();
        }

        // Trim only to recognise the literal. The guard gets the text exactly as generated.
        if (string.Equals(text.Trim(), NoQueryLiteral, StringComparison.Ordinal))
        {
            return SearchResult.Rejected(SearchReasons.NoQuery);
        }

        var verdict = guard.Validate(text);
        if (verdict.Approved is not { } approved)
        {
            return SearchResult.Rejected(verdict.Reason!);
        }

        var execution = await executor.ExecuteAsync(approved, ct);
        return execution.Reason is { } reason
            ? SearchResult.Rejected(reason)
            : SearchResult.Answered(approved, execution.Rows);
    }
}
