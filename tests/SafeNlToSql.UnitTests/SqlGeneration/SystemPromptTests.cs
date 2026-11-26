using SafeNlToSql.Application;
using SafeNlToSql.Infrastructure;

namespace SafeNlToSql.UnitTests.SqlGeneration;

public class SystemPromptTests
{
    [Theory]
    [InlineData("CertificateNumber")]
    [InlineData("EquipmentName")]
    [InlineData("EquipmentType")]
    [InlineData("Metric")]
    [InlineData("CalibrationDate")]
    [InlineData("IsCompliant")]
    public void Prompt_names_each_allowed_column(string column)
    {
        Assert.Contains(column, SystemPrompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_never_mentions_ClientName()
    {
        // An empty prompt would pass the checks below without proving anything.
        Assert.NotEmpty(SystemPrompt.Text);
        Assert.DoesNotContain("ClientName", SystemPrompt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client", SystemPrompt.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_names_dbo_Certificates()
    {
        Assert.Contains("dbo.Certificates", SystemPrompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_defines_NO_QUERY_literal()
    {
        Assert.Contains("NO_QUERY", SystemPrompt.Text, StringComparison.Ordinal);
    }

    // The model copies examples, so one that SqlGuard rejects would teach it to be rejected.
    [Fact]
    public void Every_example_query_passes_SqlGuard()
    {
        var lines = SystemPrompt.Text.Split('\n').Select(line => line.Trim()).ToList();
        var examples = lines.Where(IsExampleQuery).ToList();

        Assert.NotEmpty(examples);
        // An example is a query on its own line. One written inside a sentence would not be checked.
        Assert.DoesNotContain(
            lines, line => !IsExampleQuery(line) && line.Contains("FROM dbo.Certificates", StringComparison.Ordinal));
        var guard = new SqlGuard();
        Assert.All(examples, example =>
        {
            var verdict = guard.Validate(example);
            Assert.True(verdict.IsAllowed, $"{verdict.Reason}: {example}");
        });
    }

    private static bool IsExampleQuery(string line) => line.StartsWith("SELECT ", StringComparison.Ordinal);
}
