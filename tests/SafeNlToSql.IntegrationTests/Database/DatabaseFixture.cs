using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace SafeNlToSql.IntegrationTests.Database;

/// <summary>
/// Starts one SQL Server container and runs the same db/*.sql scripts that docker compose runs,
/// with the same tool (sqlcmd), so GO batches and the READER_PASSWORD variable behave identically.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    // Must match docker-compose.yml (checked by SchemaAndSeedTests.Compose_and_tests_use_same_sql_image).
    public const string SqlImage = "mcr.microsoft.com/mssql/server:2022-CU28-ubuntu-22.04";

    public const string ReaderLogin = "nl_search_reader";
    public const string DatabaseName = "SafeNlToSql";

    private const string SqlCmdPath = "/opt/mssql-tools18/bin/sqlcmd";

    // Random per run. Letters, digits and "!" satisfy the complexity policy and avoid
    // characters that are special in sqlcmd variables, T-SQL literals and connection strings.
    private readonly string _saPassword = $"Sa{Guid.NewGuid():N}!1";
    private readonly string _readerPassword = $"Rd{Guid.NewGuid():N}!1";

    private MsSqlContainer? _container;

    public string SaConnectionString { get; private set; } = "";

    public string ReaderConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder(SqlImage)
            .WithPassword(_saPassword)
            .Build();
        await _container.StartAsync();

        var sa = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = DatabaseName,
            TrustServerCertificate = true,
            Pooling = false,
        };
        SaConnectionString = sa.ConnectionString;

        ReaderConnectionString = new SqlConnectionStringBuilder(sa.ConnectionString)
        {
            UserID = ReaderLogin,
            Password = _readerPassword,
        }.ConnectionString;

        foreach (var path in ScriptPaths())
        {
            await _container.CopyAsync(File.ReadAllBytes(path), ContainerPath(path));
        }

        await RunScriptsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Runs every db/*.sql file in name order, exactly as the db-init compose service does.</summary>
    public async Task RunScriptsAsync()
    {
        foreach (var path in ScriptPaths())
        {
            var result = await _container!.ExecAsync(
            [
                SqlCmdPath, "-S", "localhost", "-U", "sa", "-P", _saPassword,
                "-C", "-b", "-I",
                "-v", $"READER_PASSWORD={_readerPassword}",
                "-i", ContainerPath(path),
            ]);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} failed with exit code {result.ExitCode}.\n{result.Stdout}\n{result.Stderr}");
            }
        }
    }

    private static string ContainerPath(string path) => "/db/" + Path.GetFileName(path);

    private static List<string> ScriptPaths()
    {
        var paths = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "db"), "*.sql")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (paths.Count == 0)
        {
            throw new InvalidOperationException("No db/*.sql files were copied to the test output folder.");
        }

        return paths;
    }
}

[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
