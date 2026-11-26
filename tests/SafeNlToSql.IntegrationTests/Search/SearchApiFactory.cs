using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SafeNlToSql.Application;
using SafeNlToSql.IntegrationTests.Database;

namespace SafeNlToSql.IntegrationTests.Search;

/// <summary>
/// Runs the real Program against the test container, connected as nl_search_reader. Only configuration
/// and the ISqlGenerator registration are replaced.
/// </summary>
public sealed class SearchApiFactory(DatabaseFixture db, bool useFakeGenerator = true, string environment = "Production")
    : WebApplicationFactory<Program>
{
    public FakeSqlGenerator Generator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The default is Production, which is what clients see: no developer exception page, and no local user-secrets.
        builder.UseEnvironment(environment);

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
}
