using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SafeNlToSql.Application;
using SafeNlToSql.Infrastructure;

namespace SafeNlToSql.UnitTests.SqlGeneration;

public class AzureOpenAiRegistrationTests
{
    private const string Endpoint = "https://unit-test.openai.azure.com/openai/v1/";
    private const string ApiKey = "KEY-SENTINEL";

    [Fact]
    public void Complete_section_registers_Azure_generator_with_defaults()
    {
        using var provider = Register(Settings());

        Assert.IsType<AzureOpenAiSqlGenerator>(provider.GetRequiredService<ISqlGenerator>());
        var options = provider.GetRequiredService<AzureOpenAiOptions>();
        Assert.Equal(Endpoint, options.Endpoint);
        Assert.Equal("test-deployment", options.Deployment);
        Assert.Equal(ApiKey, options.ApiKey);
        Assert.Equal(15, options.TimeoutSeconds);
        Assert.Equal(300, options.MaxOutputTokens);
    }

    [Theory]
    [InlineData("TimeoutSeconds", 1)]
    [InlineData("MaxOutputTokens", 16)]
    public void Minimum_valid_value_is_accepted(string key, int value)
    {
        using var provider = Register(Settings(key, value.ToString()));

        Assert.IsType<AzureOpenAiSqlGenerator>(provider.GetRequiredService<ISqlGenerator>());
        var options = provider.GetRequiredService<AzureOpenAiOptions>();
        Assert.Equal(value, key == "TimeoutSeconds" ? options.TimeoutSeconds : options.MaxOutputTokens);
    }

    [Theory]
    [InlineData("https://unit-test.openai.azure.com/openai/v1")]
    [InlineData("https://unit-test.openai.azure.com/openai/v1/")]
    public void Endpoint_with_or_without_trailing_slash_is_normalised(string endpoint)
    {
        using var provider = Register(Settings("Endpoint", endpoint));

        Assert.Equal(Endpoint, provider.GetRequiredService<AzureOpenAiOptions>().Endpoint);
    }

    [Theory]
    [InlineData("https://secret-resource.openai.azure.com")]
    [InlineData("https://secret-resource.openai.azure.com/")]
    [InlineData("https://secret-resource.openai.azure.com/openai/")]
    [InlineData("https://secret-resource.openai.azure.com/openai/v2/")]
    [InlineData("https://secret-resource.openai.azure.com/openai/v1//")]
    [InlineData("https://secret-resource.openai.azure.com/openai/v1/responses")]
    [InlineData("https://secret-resource.openai.azure.com/openai/deployments/x")]
    [InlineData("https://secret-resource.openai.azure.com/notopenai/v1/")]
    [InlineData("https://secret-resource.openai.azure.com/openai/v1/?api-version=preview")]
    [InlineData("https://secret-resource.openai.azure.com/openai/v1/#fragment")]
    public void Endpoint_without_openai_v1_path_throws_without_value(string endpoint)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Register(Settings("Endpoint", endpoint)));

        Assert.Contains("AzureOpenAI:Endpoint", exception.Message);
        Assert.Contains("/openai/v1/", exception.Message);
        Assert.DoesNotContain("secret-resource", exception.ToString());
    }

    [Theory]
    [InlineData("Endpoint", null)]
    [InlineData("Endpoint", "")]
    [InlineData("Endpoint", "http://unit-test.openai.azure.com/openai/v1/")]
    [InlineData("Endpoint", "/openai/v1/")]
    [InlineData("Deployment", null)]
    [InlineData("Deployment", "   ")]
    [InlineData("ApiKey", null)]
    [InlineData("ApiKey", "   ")]
    [InlineData("TimeoutSeconds", "0")]
    [InlineData("TimeoutSeconds", "-1")]
    [InlineData("MaxOutputTokens", "0")]
    [InlineData("MaxOutputTokens", "15")]
    public void Incomplete_or_invalid_section_throws_naming_the_key(string key, string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Register(Settings(key, value)));

        Assert.Contains("AzureOpenAI:" + key, exception.Message);
        Assert.DoesNotContain(ApiKey, exception.ToString());
    }

    /// <summary>A complete section, with one key replaced, or removed when the value is null.</summary>
    private static Dictionary<string, string?> Settings(string? key = null, string? value = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = Endpoint,
            ["AzureOpenAI:Deployment"] = "test-deployment",
            ["AzureOpenAI:ApiKey"] = ApiKey,
        };

        if (key is not null)
        {
            if (value is null)
            {
                settings.Remove("AzureOpenAI:" + key);
            }
            else
            {
                settings["AzureOpenAI:" + key] = value;
            }
        }

        return settings;
    }

    private static ServiceProvider Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddAzureOpenAiSqlGenerator(configuration);
        return services.BuildServiceProvider();
    }
}
