using SafeNlToSql.Application;

namespace SafeNlToSql.Api;

internal sealed record SearchRequest(string? Question);

internal sealed record SearchResponse(
    string? Sql, bool Rejected, string? Reason, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

internal static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/search", SearchAsync);
        return app;
    }

    private static async Task<IResult> SearchAsync(SearchRequest request, SearchService search, CancellationToken ct)
    {
        var question = request.Question;
        if (string.IsNullOrWhiteSpace(question) || question.Length > SearchService.MaxQuestionLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = [$"A question of at most {SearchService.MaxQuestionLength} characters is required."],
            });
        }

        var result = await search.SearchAsync(question, ct);
        if (result.Outcome == SearchOutcome.GeneratorUnavailable)
        {
            // A fixed message only: no exception text, model output or SQL.
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The query generator is unavailable. Try again later.");
        }

        return Results.Ok(new SearchResponse(
            result.Sql, result.Outcome == SearchOutcome.Rejected, result.Reason, result.Rows));
    }
}
