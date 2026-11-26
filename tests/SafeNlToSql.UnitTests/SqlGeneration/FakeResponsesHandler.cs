using System.Net;
using System.Text;
using System.Text.Json;

namespace SafeNlToSql.UnitTests.SqlGeneration;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

/// <summary>Stands in for the network under the OpenAI client: records each attempt and returns a canned response.</summary>
internal sealed class FakeResponsesHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>A 200 with a response object. A null status leaves the property out of the JSON.</summary>
    public static FakeResponsesHandler Returning(
        string outputText, string? status = "completed", string? incompleteReason = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["id"] = "resp_test",
            ["object"] = "response",
            ["created_at"] = 1,
            ["model"] = "test-model",
            ["incomplete_details"] = incompleteReason is null ? null : new { reason = incompleteReason },
            ["output"] = new[]
            {
                new
                {
                    type = "message",
                    id = "msg_test",
                    status = "completed",
                    role = "assistant",
                    content = new[] { new { type = "output_text", text = outputText, annotations = Array.Empty<object>() } },
                },
            },
            ["parallel_tool_calls"] = true,
            ["tools"] = Array.Empty<object>(),
            ["tool_choice"] = "auto",
        };
        if (status is not null)
        {
            body["status"] = status;
        }

        var json = JsonSerializer.Serialize(body);
        return new(_ => Task.FromResult(Json(HttpStatusCode.OK, json)));
    }

    public static FakeResponsesHandler Failing(
        HttpStatusCode status, int? retryAfterSeconds = null, string body = """{"error":{"message":"test error"}}""") =>
        new(_ =>
        {
            var response = Json(status, body);
            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", seconds.ToString());
            }

            return Task.FromResult(response);
        });

    /// <summary>Fails every attempt before there is a response, as DNS or socket errors do.</summary>
    public static FakeResponsesHandler Unreachable(Func<Exception> failure) =>
        new(_ => Task.FromException<HttpResponseMessage>(failure()));

    /// <summary>Never answers; only cancellation ends the request.</summary>
    public static FakeResponsesHandler Hanging() =>
        new(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new RecordedRequest(
            request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        return await respond(ct);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
