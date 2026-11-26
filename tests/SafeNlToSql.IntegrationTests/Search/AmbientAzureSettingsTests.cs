using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SafeNlToSql.Api;
using SafeNlToSql.Application;
using SafeNlToSql.IntegrationTests.Database;

namespace SafeNlToSql.IntegrationTests.Search;

/// <summary>
/// A test host must not see the AzureOpenAI settings of the machine it runs on, or a plain dotnet test could
/// call Azure, or fail at startup. These tests inject fake environment variables and a fake user-secrets
/// file, never real ones. In the Database collection, so no other host starts while they are in place.
/// </summary>
[Collection("Database")]
public class AmbientAzureSettingsTests(DatabaseFixture db)
{
    // The host strips the DOTNET_ prefix, so those names reach the same settings. There is no ASPNETCORE_
    // case: those variables also reach a test host as WebApplicationFactory's command-line arguments, which the
    // factory keeps. Filtering them would be fragile for a very unlikely case, and the one test that posts with
    // the real registration, Default_generator_returns_503, checks the generator first. See docs/SPEC.md.
    [Theory]
    [InlineData("")]
    [InlineData("DOTNET_")]
    public void Complete_environment_settings_do_not_register_Azure_generator(string prefix)
    {
        using var ambient = new FakeEnvironment(
            (prefix + "AzureOpenAI__Endpoint", "https://ambient-fake.invalid/openai/v1/"),
            (prefix + "AzureOpenAI__Deployment", "ambient-fake-deployment"),
            (prefix + "AzureOpenAI__ApiKey", "ambient-fake-key"));
        using var factory = new SearchApiFactory(db, useFakeGenerator: false);

        Assert.IsType<NotConfiguredSqlGenerator>(factory.Services.GetRequiredService<ISqlGenerator>());
        Assert.False(factory.Services.GetRequiredService<IConfiguration>().GetSection("AzureOpenAI").Exists());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Partial_environment_settings_do_not_fail_startup(bool useFakeGenerator)
    {
        using var ambient = new FakeEnvironment(
            ("AzureOpenAI__Endpoint", "https://ambient-fake.invalid/openai/v1/"));
        using var factory = new SearchApiFactory(db, useFakeGenerator);

        var generator = factory.Services.GetRequiredService<ISqlGenerator>();

        Assert.IsType(useFakeGenerator ? typeof(FakeSqlGenerator) : typeof(NotConfiguredSqlGenerator), generator);
    }

    // Only a Development host reads user-secrets. The file here is a fake one in a temporary directory.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Development_host_does_not_load_user_secrets(bool completeSection)
    {
        var settings = new Dictionary<string, string>
        {
            ["AzureOpenAI:Endpoint"] = "https://secrets-fake.invalid/openai/v1/",
        };
        if (completeSection)
        {
            settings["AzureOpenAI:Deployment"] = "secrets-fake-deployment";
            settings["AzureOpenAI:ApiKey"] = "secrets-fake-key";
        }

        // Declared before the factory, so the host is disposed while the lookup is still redirected.
        using var secrets = new FakeUserSecrets(settings);
        using var factory = new SearchApiFactory(db, useFakeGenerator: false, environment: "Development");

        Assert.True(factory.Services.GetRequiredService<IHostEnvironment>().IsDevelopment());
        Assert.IsType<NotConfiguredSqlGenerator>(factory.Services.GetRequiredService<ISqlGenerator>());
        Assert.False(factory.Services.GetRequiredService<IConfiguration>().GetSection("AzureOpenAI").Exists());
    }

    /// <summary>Sets variables in this test process only, and removes them again.</summary>
    private sealed class FakeEnvironment : IDisposable
    {
        private readonly string[] _names;

        public FakeEnvironment(params (string Name, string Value)[] variables)
        {
            _names = variables.Select(v => v.Name).ToArray();
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var name in _names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    /// <summary>
    /// A fake user-secrets file for the Api project, in a new temporary directory. Until Dispose, APPDATA in
    /// this process points to that directory, and the user-secrets lookup starts from APPDATA. The class writes
    /// only the file it creates there, reads none, and deletes only that directory.
    /// </summary>
    private sealed class FakeUserSecrets : IDisposable
    {
        private readonly string? _previousAppData = Environment.GetEnvironmentVariable("APPDATA");

        // New and empty, so no path inside it can be a file that already existed.
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("fake-user-secrets-");

        public FakeUserSecrets(Dictionary<string, string> settings)
        {
            try
            {
                Environment.SetEnvironmentVariable("APPDATA", _root.FullName);

                // Only computes the path; it opens nothing.
                var id = typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId;
                var path = Path.GetFullPath(PathHelper.GetSecretsPathFromSecretsId(id));

                // Before anything is created or written: stop if the redirect did not work.
                RequireRedirected(
                    path.StartsWith(_root.FullName + Path.DirectorySeparatorChar, StringComparison.Ordinal));

                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);

                // CreateNew fails rather than overwrite.
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    JsonSerializer.Serialize(file, settings);
                }

                // The same lookup a Development host makes, checked before any host exists. Without Build,
                // the file is not read.
                var lookup = new ConfigurationBuilder().AddUserSecrets(typeof(Program).Assembly);
                RequireRedirected(
                    lookup.Sources.Single() is JsonConfigurationSource { FileProvider: PhysicalFileProvider files }
                    && files.Root == directory + Path.DirectorySeparatorChar);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("APPDATA", _previousAppData);
            _root.Delete(recursive: true);
        }

        private static void RequireRedirected(bool redirected) =>
            Assert.True(redirected, "The user-secrets lookup was not redirected to the temporary directory.");
    }
}
