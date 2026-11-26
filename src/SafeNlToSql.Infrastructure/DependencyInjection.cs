using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SafeNlToSql.Application;

namespace SafeNlToSql.Infrastructure;

public static class DependencyInjection
{
    // The smallest max_output_tokens the Responses API accepts.
    private const int MinOutputTokens = 16;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Reader");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Reader is not configured. Set it with dotnet user-secrets locally or an environment variable in CI.");
        }

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(o => o.CommandTimeoutSeconds > 0, "Database:CommandTimeoutSeconds must be greater than 0.")
            .ValidateOnStart();

        services.AddSingleton<IQueryExecutor>(sp =>
            new DapperQueryExecutor(
                connectionString,
                sp.GetRequiredService<IOptions<DatabaseOptions>>(),
                sp.GetRequiredService<ILogger<DapperQueryExecutor>>()));

        return services;
    }

    /// <summary>
    /// Registers the SQL generator: Azure OpenAI when the AzureOpenAI section exists, otherwise the one from
    /// <paramref name="whenNotConfigured"/>. The section is read from the built host's configuration, when the
    /// generator is first resolved. An incomplete or invalid section throws then, so the app resolves the
    /// generator once at startup.
    /// </summary>
    public static IServiceCollection AddSqlGenerator(
        this IServiceCollection services, Func<IServiceProvider, ISqlGenerator> whenNotConfigured)
    {
        // Only the Azure generator asks for these, so they are not validated unless it is created.
        services.AddSingleton(sp => ReadAzureOpenAiOptions(sp.GetRequiredService<IConfiguration>()));

        services.AddSingleton<ISqlGenerator>(sp =>
            sp.GetRequiredService<IConfiguration>().GetSection(AzureOpenAiOptions.SectionName).Exists()
                ? new AzureOpenAiSqlGenerator(
                    sp.GetRequiredService<AzureOpenAiOptions>(),
                    sp.GetRequiredService<ILogger<AzureOpenAiSqlGenerator>>())
                : whenNotConfigured(sp));

        return services;
    }

    /// <summary>
    /// Throws when the AzureOpenAI section is incomplete or invalid. The messages name the setting and never
    /// include a value: one is a secret.
    /// </summary>
    private static AzureOpenAiOptions ReadAzureOpenAiOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(AzureOpenAiOptions.SectionName).Get<AzureOpenAiOptions>()
            ?? new AzureOpenAiOptions();

        options.Endpoint = NormaliseAzureEndpoint(options.Endpoint);
        Require(!string.IsNullOrWhiteSpace(options.Deployment), "AzureOpenAI:Deployment is not configured.");
        Require(
            !string.IsNullOrWhiteSpace(options.ApiKey),
            "AzureOpenAI:ApiKey is not configured. Set it with dotnet user-secrets locally.");
        Require(options.TimeoutSeconds > 0, "AzureOpenAI:TimeoutSeconds must be greater than 0.");
        Require(
            options.MaxOutputTokens >= MinOutputTokens,
            $"AzureOpenAI:MaxOutputTokens must be at least {MinOutputTokens}.");

        return options;
    }

    /// <summary>Accepts the v1 endpoint with or without the trailing slash and returns it with the slash.</summary>
    private static string NormaliseAzureEndpoint(string endpoint)
    {
        const string message = "AzureOpenAI:Endpoint must be an absolute https URI ending with /openai/v1/";

        Require(
            Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Query.Length == 0
                && uri.Fragment.Length == 0,
            message);

        var url = uri!.GetLeftPart(UriPartial.Path);
        if (!url.EndsWith('/'))
        {
            url += "/";
        }

        Require(url.EndsWith("/openai/v1/", StringComparison.Ordinal), message);
        return url;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
