namespace SafeNlToSql.IntegrationTests.Database;

/// <summary>
/// Connects as nl_search_reader without SqlGuard in between: proves that layer 3 (the database)
/// holds on its own, even if the prompt and the guard both failed.
/// </summary>
[Collection("Database")]
public class ReaderPermissionTests(DatabaseFixture db)
{
    [Fact]
    public async Task Reader_can_select_allowed_columns()
    {
        var names = await SqlHelper.FirstColumnAsync(db.ReaderConnectionString,
            """
            SELECT TOP 5 CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant
            FROM dbo.Certificates
            """);

        Assert.Equal(5, names.Count);
    }

    // With column-level SELECT only, SQL Server demands SELECT on every column of a table when a query
    // references no granted column of it. SqlGuard does not copy this rule (both layers fail closed).
    [Theory]
    [InlineData("SELECT COUNT(*) FROM dbo.Certificates")]
    [InlineData("SELECT COUNT(1) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 1 1 FROM dbo.Certificates")]
    [InlineData("SELECT TOP 5 EquipmentName FROM dbo.Certificates WHERE EXISTS (SELECT 1 FROM dbo.Certificates)")]
    public async Task Reader_is_denied_when_no_allowed_column_is_referenced(string sql)
    {
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString, sql, 230);
    }

    [Fact]
    public async Task Reader_can_count_star_with_filter_on_allowed_column()
    {
        const string sql = "SELECT COUNT(*) FROM dbo.Certificates WHERE IsCompliant = 0";

        var asReader = await SqlHelper.ScalarAsync<int>(db.ReaderConnectionString, sql);
        var asSa = await SqlHelper.ScalarAsync<int>(db.SaConnectionString, sql);

        Assert.Equal(asSa, asReader);
        Assert.True(asSa > 0, "the seed should contain non-compliant rows");
    }

    [Fact]
    public async Task Reader_cannot_select_ClientName()
    {
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString,
            "SELECT TOP 1 ClientName FROM dbo.Certificates", 230);
    }

    [Fact]
    public async Task Reader_cannot_filter_on_ClientName()
    {
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString,
            "SELECT TOP 1 EquipmentName FROM dbo.Certificates WHERE ClientName = N'x'", 230);
    }

    [Fact]
    public async Task Reader_cannot_select_star()
    {
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString,
            "SELECT TOP 1 * FROM dbo.Certificates", 230);
    }

    [Fact]
    public async Task Reader_cannot_read_Clients_table()
    {
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString,
            "SELECT TOP 1 Name FROM dbo.Clients", 229);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Certificates (CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant, ClientName) VALUES (N'X', N'X', N'X', N'X', GETDATE(), 1, N'X')")]
    [InlineData("UPDATE dbo.Certificates SET IsCompliant = 1")]
    [InlineData("DELETE FROM dbo.Certificates")]
    public async Task Reader_cannot_modify_Certificates(string sql)
    {
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString, sql, 229);

        // Belt and braces: the data really is untouched.
        Assert.Equal(50, await SqlHelper.ScalarAsync<int>(db.SaConnectionString, "SELECT COUNT(*) FROM dbo.Certificates"));
    }

    [Fact]
    public async Task Reader_cannot_create_table()
    {
        // A permanent table: any login may create #temp tables in tempdb, so that would prove nothing.
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString, "CREATE TABLE dbo.X (Id int)", 262);

        // And the table really does not exist (OBJECT_ID returns NULL).
        Assert.Equal(DBNull.Value, await SqlHelper.ScalarAsync<object>(db.SaConnectionString, "SELECT OBJECT_ID('dbo.X')"));
    }

    [Fact]
    public async Task Reader_cannot_execute_xp_cmdshell()
    {
        await SqlHelper.AssertDeniedAsync(db.ReaderConnectionString, "EXEC xp_cmdshell 'echo hi'", 229);
    }

    [Fact]
    public async Task Reader_sees_only_Certificates_in_sys_tables()
    {
        var tables = await SqlHelper.FirstColumnAsync(db.ReaderConnectionString, "SELECT name FROM sys.tables ORDER BY name");

        Assert.Equal(["Certificates"], tables);
    }
}
