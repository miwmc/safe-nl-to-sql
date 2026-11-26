using System.Collections.Concurrent;
using SafeNlToSql.Application;

namespace SafeNlToSql.IntegrationTests.Search;

/// <summary>Returns canned SQL per question (exact match) and records every question it was asked.</summary>
public sealed class FakeSqlGenerator : ISqlGenerator
{
    private readonly ConcurrentDictionary<string, string> _answers = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Questions { get; } = new();

    public void Add(string question, string sql) => _answers[question] = sql;

    public Task<string> GenerateAsync(string question, CancellationToken ct)
    {
        Questions.Enqueue(question);

        // An unknown question is a mistake in the test, so it must not pass as some default answer.
        return _answers.TryGetValue(question, out var sql)
            ? Task.FromResult(sql)
            : throw new InvalidOperationException($"FakeSqlGenerator has no answer for: {question}");
    }
}
