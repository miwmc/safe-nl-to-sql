using Microsoft.Extensions.Configuration;
using SafeNlToSql.Infrastructure;

namespace SafeNlToSql.IntegrationTests.LiveAzureOpenAi;

/// <summary>
/// Whether the live tests run, and the AzureOpenAI settings they use, read when the tests run from the Api
/// project's user-secrets and then from environment variables. The settings hold the API key and stay in
/// memory: never write them to test output or put them in an assertion message.
/// </summary>
internal static class LiveAzureOpenAiConfig
{
    private const string OptInVariable = "LIVE_AZURE_OPENAI";

    public const string SkipReason =
        "Live Azure OpenAI tests are opt-in. They need both AzureOpenAI:ApiKey to be configured and the " +
        OptInVariable + " environment variable to be switched on.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Loaded = new(Load);

    /// <summary>Every AzureOpenAI:* setting that has a value, under its full key.</summary>
    public static IReadOnlyDictionary<string, string> Settings => Loaded.Value;

    /// <summary>
    /// A configured key is not enough: the calls cost money, so a run must also ask for them, with the
    /// variable set to exactly "1". It is checked first, so without it the user-secrets are not even read.
    /// </summary>
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "1", StringComparison.Ordinal)
        && HasApiKey;

    private static bool HasApiKey =>
        Settings.TryGetValue($"{AzureOpenAiOptions.SectionName}:{nameof(AzureOpenAiOptions.ApiKey)}", out var key)
        && !string.IsNullOrWhiteSpace(key);

    private static Dictionary<string, string> Load() =>
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetSection(AzureOpenAiOptions.SectionName)
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value!, StringComparer.OrdinalIgnoreCase);
}
