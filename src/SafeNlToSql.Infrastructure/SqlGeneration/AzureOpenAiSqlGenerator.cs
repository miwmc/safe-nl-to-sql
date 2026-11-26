// The Responses API types in OpenAI 2.15.0 are still marked experimental (OPENAI001). Suppressed in this
// file only: it is the one place that uses them, and nothing experimental appears in its public surface.
#pragma warning disable OPENAI001

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using SafeNlToSql.Application;

namespace SafeNlToSql.Infrastructure;

/// <summary>
/// Asks an Azure OpenAI deployment for SQL through the Responses API on the v1 endpoint. Returns the output
/// text exactly as received, without stripping or repairing it: SqlGuard decides whether it may run.
/// Anything but a completed response throws: GeneratorRefusedException when the content filter blocked the
/// question or the answer, and another exception for every failure.
/// </summary>
public sealed class AzureOpenAiSqlGenerator : ISqlGenerator
{
    private const int MaxRetries = 1;
    private const int MaxErrorCodeLength = 64;
    private const string ContentFilterCode = "content_filter";

    private readonly ResponsesClient _client;
    private readonly string _deployment;
    private readonly int _maxOutputTokens;
    private readonly TimeSpan _timeout;
    private readonly ILogger<AzureOpenAiSqlGenerator> _logger;

    /// <param name="transport">Replaces the network in tests. Null uses the SDK's default transport.</param>
    public AzureOpenAiSqlGenerator(
        AzureOpenAiOptions options, ILogger<AzureOpenAiSqlGenerator> logger, PipelineTransport? transport = null)
    {
        _deployment = options.Deployment;
        _maxOutputTokens = options.MaxOutputTokens;
        _timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _logger = logger;

        var clientOptions = new ResponsesClientOptions
        {
            Endpoint = new Uri(options.Endpoint),
            RetryPolicy = new ClientRetryPolicy(MaxRetries),
            // Per attempt only. The bound on the whole call is in GenerateAsync.
            NetworkTimeout = _timeout,
        };
        if (transport is not null)
        {
            clientOptions.Transport = transport;
        }

        _client = new ResponsesClient(new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    public async Task<string> GenerateAsync(string question, CancellationToken ct)
    {
        // The question goes only into the user message, never into the instructions.
        // No tools and no previous response: each question stands alone.
        var request = new CreateResponseOptions
        {
            Model = _deployment,
            Instructions = SystemPrompt.Text,
            StoredOutputEnabled = false,
            Temperature = 0,
            MaxOutputTokenCount = _maxOutputTokens,
        };
        request.InputItems.Add(ResponseItem.CreateUserMessageItem(question));

        // One budget for the attempts and the wait between them. Without it, a long Retry-After would be
        // honoured however long it is.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_timeout);

        ResponseResult response;
        try
        {
            response = await _client.CreateResponseAsync(request, budget.Token);
        }
        catch (Exception ex) when (IsCancellation(ex))
        {
            // Fresh exceptions, with no inner one: after a failed first attempt the SDK's exception also
            // holds that attempt's error. The caller's cancellation is thrown first; otherwise it was ours.
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"Azure OpenAI did not answer within {_timeout.TotalSeconds} seconds.");
        }
        catch (ClientResultException ex)
        {
            throw RequestFailed(ex);
        }
        catch (AggregateException ex) when (!IsCancellation(ex))
        {
            // Thrown when the attempts themselves threw, as network errors do. The last attempt decides.
            throw RequestFailed(ex.InnerExceptions.LastOrDefault() as ClientResultException);
        }

        // An incomplete response still carries its partial text, and a truncated query can be valid SQL
        // that is broader than the question. So the status is checked before the text is read.
        if (response.Status != ResponseStatus.Completed)
        {
            var description =
                $"Azure OpenAI response status {response.Status?.ToString() ?? "missing"} " +
                $"(reason {response.IncompleteStatusDetails?.Reason?.ToString() ?? "none"})";
            var filtered = response.Status == ResponseStatus.Incomplete
                && response.IncompleteStatusDetails?.Reason == ResponseIncompleteStatusReason.ContentFilter;
            throw filtered
                ? new GeneratorRefusedException(description)
                : new InvalidOperationException(description);
        }

        var sql = response.GetOutputText();
        _logger.LogInformation("Generated SQL: {Sql}", sql);
        return sql;
    }

    // When a retry is cancelled, the SDK wraps the cancellation in an AggregateException.
    private static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException
        || (exception is AggregateException aggregate
            && aggregate.InnerExceptions.Any(inner => inner is OperationCanceledException));

    /// <summary>
    /// What a failed request is reported as. The SDK's exceptions carry the server's error message, which
    /// could quote the question, or the host name and socket error, and the caller logs the whole exception.
    /// So only the status and a checked error code are carried over, and the original is deliberately not
    /// kept as the inner exception. A 400 from the content filter is a refusal, because a retry can never help.
    /// </summary>
    private static Exception RequestFailed(ClientResultException? exception)
    {
        // A status of 0 means there was no response.
        if (exception is null || exception.Status == 0)
        {
            return new HttpRequestException("Azure OpenAI request failed: no response");
        }

        var status = (HttpStatusCode)exception.Status;
        var code = ReadErrorCode(exception);
        var description = $"Azure OpenAI request failed with HTTP {exception.Status} (code {code})";

        return status == HttpStatusCode.BadRequest && code == ContentFilterCode
            ? new GeneratorRefusedException(description)
            : new HttpRequestException(description, inner: null, statusCode: status);
    }

    /// <summary>
    /// error.code from the raw response, or error.type when there is no code, and only if it is a short
    /// identifier. Everything else is "unknown".
    /// </summary>
    private static string ReadErrorCode(ClientResultException exception)
    {
        const string unknown = "unknown";

        if (exception.GetRawResponse() is not { } response)
        {
            return unknown;
        }

        try
        {
            using var body = JsonDocument.Parse(response.Content.ToMemory());
            if (body.RootElement.ValueKind != JsonValueKind.Object
                || !body.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return unknown;
            }

            if (!error.TryGetProperty("code", out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (!error.TryGetProperty("type", out value))
                {
                    return unknown;
                }
            }

            return value.ValueKind == JsonValueKind.String && value.GetString() is { } text && IsShortIdentifier(text)
                ? text
                : unknown;
        }
        catch (JsonException)
        {
            return unknown;
        }
    }

    private static bool IsShortIdentifier(string value) =>
        value.Length is > 0 and <= MaxErrorCodeLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}
