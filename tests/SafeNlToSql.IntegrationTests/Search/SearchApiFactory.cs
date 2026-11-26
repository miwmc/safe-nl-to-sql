using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SafeNlToSql.Application;
using SafeNlToSql.IntegrationTests.Database;

namespace SafeNlToSql.IntegrationTests.Search;

/// <summary>
/// Runs the real Program against the test container, connected as nl_search_reader. Only configuration
/// and the ISqlGenerator registration are replaced. The machine's environment variables and user-secrets
/// are removed from the host's configuration sources.
/// </summary>
public sealed class SearchApiFactory(DatabaseFixture db, bool useFakeGenerator = true, string environment = "Production")
    : WebApplicationFactory<Program>
{
    public FakeSqlGenerator Generator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The default is Production, which is what clients see: no developer exception page.
        builder.UseEnvironment(environment);

        // Every environment variable source goes, the DOTNET_ one too: the host strips the prefix, so it could
        // carry AzureOpenAI settings. ASPNETCORE_ variables also arrive as command-line arguments, which stay
        // (a known limitation, see docs/SPEC.md). The factory passes its own host settings with UseSetting.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            foreach (var source in configuration.Sources.Where(IsMachineSource).ToArray())
            {
                configuration.Sources.Remove(source);
            }
        });

        // UseSetting, because AddInfrastructure reads the connection string while Program registers services.
        builder.UseSetting("ConnectionStrings:Reader", db.ReaderConnectionString);

        if (useFakeGenerator)
        {
            // Runs after Program's own registrations, so this replaces whatever Program registered.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISqlGenerator>();
                services.AddSingleton<ISqlGenerator>(Generator);
            });
        }
    }

    // secrets.json is the file name of the user-secrets source; appsettings files are kept.
    private static bool IsMachineSource(IConfigurationSource source) =>
        source is EnvironmentVariablesConfigurationSource or JsonConfigurationSource { Path: "secrets.json" };
}
