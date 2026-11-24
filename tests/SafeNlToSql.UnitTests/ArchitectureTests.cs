using SafeNlToSql.Application;

namespace SafeNlToSql.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void Application_does_not_reference_Dapper_SqlClient_or_Infrastructure()
    {
        var referenced = typeof(SqlGuard).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToList();

        string[] forbidden = ["Dapper", "Microsoft.Data.SqlClient", "System.Data.SqlClient", "SafeNlToSql.Infrastructure"];

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(referenced, r => r.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
