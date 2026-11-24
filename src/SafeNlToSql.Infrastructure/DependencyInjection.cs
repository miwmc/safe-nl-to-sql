using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SafeNlToSql.Application;

namespace SafeNlToSql.Infrastructure;

public static class DependencyInjection
{
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
            new DapperQueryExecutor(connectionString, sp.GetRequiredService<IOptions<DatabaseOptions>>()));

        return services;
    }
}
