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
}
