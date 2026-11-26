using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SafeNlToSql.Application;
using SafeNlToSql.Infrastructure;
using SafeNlToSql.IntegrationTests.Database;

namespace SafeNlToSql.IntegrationTests.Search;

/// <summary>
/// Which generator the real Program registers. Nothing here calls the model. The case without the section
/// is covered by SearchEndpointTests.Default_generator_returns_503.
/// </summary>
[Collection("Database")]
public class GeneratorRegistrationTests(DatabaseFixture db)
{
    [Fact]
    public void Configured_section_registers_Azure_generator()
    {
        using var factory = new SearchApiFactory(db, useFakeGenerator: false);
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            UsePartialSection(builder);
            builder.UseSetting("AzureOpenAI:ApiKey", "not-a-real-key");
        });

        Assert.IsType<AzureOpenAiSqlGenerator>(configured.Services.GetRequiredService<ISqlGenerator>());
    }

    [Fact]
    public void Partial_section_fails_at_startup()
    {
        using var factory = new SearchApiFactory(db, useFakeGenerator: false);
        using var partial = factory.WithWebHostBuilder(UsePartialSection);

        var exception = Assert.Throws<InvalidOperationException>(() => partial.CreateClient());

        Assert.Contains("AzureOpenAI:ApiKey", exception.Message);
    }

    // The section is passed with UseSetting, so the host really has it. Only the replaced generator keeps
    // it from being validated.
    [Fact]
    public void Fake_generator_host_never_validates_Azure_settings()
    {
        using var factory = new SearchApiFactory(db);
        using var partial = factory.WithWebHostBuilder(UsePartialSection);

        Assert.Same(factory.Generator, partial.Services.GetRequiredService<ISqlGenerator>());
        Assert.True(partial.Services.GetRequiredService<IConfiguration>().GetSection("AzureOpenAI").Exists());
    }

    private static void UsePartialSection(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureOpenAI:Endpoint", "https://integration-test.openai.azure.com/openai/v1/");
        builder.UseSetting("AzureOpenAI:Deployment", "test-deployment");
    }
}
