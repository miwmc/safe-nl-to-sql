using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SafeNlToSql.IntegrationTests.Database;

[Collection("Database")]
public class SchemaAndSeedTests(DatabaseFixture db)
{
    [Fact]
    public async Task Certificates_columns_match_spec()
    {
        var columns = await SqlHelper.FirstColumnAsync(db.SaConnectionString,
            """
            SELECT COLUMN_NAME + ' ' + DATA_TYPE
                 + CASE WHEN CHARACTER_MAXIMUM_LENGTH IS NULL THEN '' ELSE '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)) + ')' END
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Certificates'
            ORDER BY ORDINAL_POSITION
            """);

        Assert.Equal(
        [
            "Id int",
            "CertificateNumber nvarchar(50)",
            "EquipmentName nvarchar(100)",
            "EquipmentType nvarchar(50)",
            "Metric nvarchar(50)",
            "CalibrationDate date",
            "IsCompliant bit",
            "ClientName nvarchar(100)",
        ], columns);
    }

    [Fact]
    public async Task Seed_has_50_rows()
    {
        var count = await SqlHelper.ScalarAsync<int>(db.SaConnectionString, "SELECT COUNT(*) FROM dbo.Certificates");

        Assert.Equal(50, count);
    }

    [Fact]
    public async Task Seed_covers_all_equipment_types_and_metrics()
    {
        var types = await SqlHelper.FirstColumnAsync(db.SaConnectionString,
            "SELECT DISTINCT EquipmentType FROM dbo.Certificates ORDER BY 1");
        var metrics = await SqlHelper.FirstColumnAsync(db.SaConnectionString,
            "SELECT DISTINCT Metric FROM dbo.Certificates ORDER BY 1");

        Assert.Equal(["Fridge", "Microphone", "Microwave", "Speaker"], types);
        Assert.Equal(["AcousticLevel", "Frequency", "Linearity", "Temperature"], metrics);
    }

    [Fact]
    public async Task Seed_has_compliant_and_non_compliant_rows()
    {
        var values = await SqlHelper.FirstColumnAsync(db.SaConnectionString,
            "SELECT DISTINCT CAST(IsCompliant AS int) FROM dbo.Certificates ORDER BY 1");

        Assert.Equal(["0", "1"], values);
    }

    [Fact]
    public async Task Seed_dates_span_last_18_months()
    {
        // "Today" comes from the database so the container's clock and timezone cannot make this flaky.
        var today = await SqlHelper.ScalarAsync<DateTime>(db.SaConnectionString, "SELECT CAST(GETDATE() AS date)");
        var oldest = await SqlHelper.ScalarAsync<DateTime>(db.SaConnectionString, "SELECT MIN(CalibrationDate) FROM dbo.Certificates");
        var newest = await SqlHelper.ScalarAsync<DateTime>(db.SaConnectionString, "SELECT MAX(CalibrationDate) FROM dbo.Certificates");

        Assert.True(newest <= today, $"newest {newest:d} is after today {today:d}");
        Assert.True(oldest >= today.AddMonths(-18), $"oldest {oldest:d} is more than 18 months ago");
        Assert.True((newest - oldest).TotalDays > 365, "seed dates should be spread over more than 12 months");
    }

    [Fact]
    public async Task Scripts_can_run_twice()
    {
        var certificatesBefore = await SqlHelper.ScalarAsync<int>(db.SaConnectionString, "SELECT COUNT(*) FROM dbo.Certificates");
        var clientsBefore = await SqlHelper.ScalarAsync<int>(db.SaConnectionString, "SELECT COUNT(*) FROM dbo.Clients");

        await db.RunScriptsAsync();

        Assert.Equal(certificatesBefore, await SqlHelper.ScalarAsync<int>(db.SaConnectionString, "SELECT COUNT(*) FROM dbo.Certificates"));
        Assert.Equal(clientsBefore, await SqlHelper.ScalarAsync<int>(db.SaConnectionString, "SELECT COUNT(*) FROM dbo.Clients"));

        // The reader can still log in and read an allowed column (COUNT(*) is covered by ReaderPermissionTests)...
        Assert.Equal(certificatesBefore, await SqlHelper.ScalarAsync<int>(db.ReaderConnectionString, "SELECT COUNT(EquipmentName) FROM dbo.Certificates"));

        // ...and REVOKE-then-GRANT converged: ClientName is still denied (230) and the grant is still six columns.
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString, "SELECT TOP 1 ClientName FROM dbo.Certificates", 230);

        var grantedColumns = await SqlHelper.FirstColumnAsync(db.SaConnectionString,
            """
            SELECT c.name
            FROM sys.database_permissions p
            JOIN sys.columns c ON c.object_id = p.major_id AND c.column_id = p.minor_id
            WHERE p.grantee_principal_id = USER_ID('nl_search_reader')
              AND p.class = 1 AND p.major_id = OBJECT_ID('dbo.Certificates')
              AND p.permission_name = 'SELECT' AND p.state = 'G'
            ORDER BY c.name
            """);
        Assert.Equal(
            ["CalibrationDate", "CertificateNumber", "EquipmentName", "EquipmentType", "IsCompliant", "Metric"],
            grantedColumns);
    }

    [Fact]
    public async Task Compose_and_tests_use_same_sql_image()
    {
        var compose = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "docker-compose.yml"));

        var images = Regex.Matches(compose, @"^\s*image:\s*(\S+)\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(images);
        Assert.All(images, image => Assert.Equal(DatabaseFixture.SqlImage, image));
    }
}
