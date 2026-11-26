namespace SafeNlToSql.Infrastructure;

/// <summary>
/// Settings for the Azure OpenAI generator. The defaults live here and not in appsettings.json, because
/// the presence of the section is what turns the generator on.
/// </summary>
public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>The v1 endpoint: https://&lt;resource&gt;.openai.azure.com/openai/v1/</summary>
    public string Endpoint { get; set; } = "";

    public string Deployment { get; set; } = "";

    /// <summary>A secret: dotnet user-secrets locally. Never logged or put in an error message.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The bound on one whole generator call, including the retry and the wait before it.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    public int MaxOutputTokens { get; set; } = 300;
}
