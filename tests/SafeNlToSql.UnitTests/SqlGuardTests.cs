using SafeNlToSql.Application;

namespace SafeNlToSql.UnitTests;

public class SqlGuardTests
{
    private readonly SqlGuard _guard = new();

    // Rule 1: the SQL must parse.

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates")]
    public void Rule1_Parseable_sql_is_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData("SELEC TOP 10 EquipmentName FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE")]
    [InlineData("SELECT 'unterminated")]
    // Tricky: an unclosed block comment swallows the rest of the input.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates /* unclosed comment")]
    public void Rule1_Unparseable_sql_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.NotParseable, result.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public void Rule1_Empty_sql_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.Empty, result.Reason);
    }

    // Rule 2: exactly one statement.

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates;")]
    // Tricky: a semicolon inside a string literal is not a statement separator.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentName = 'a;b'")]
    // Tricky: a semicolon and a second statement inside a comment are not statements.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates -- ; DROP TABLE dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates /* ; DROP TABLE dbo.Certificates */")]
    public void Rule2_Single_statement_is_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates; DROP TABLE dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates; SELECT TOP 10 Metric FROM dbo.Certificates")]
    // Tricky: no semicolon between the statements, T-SQL still parses them as two.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates SELECT TOP 10 Metric FROM dbo.Certificates")]
    // Tricky: empty statements around a real one do not hide the second real statement.
    [InlineData(";SELECT TOP 10 EquipmentName FROM dbo.Certificates;; DROP TABLE dbo.Certificates;")]
    // Tricky: a batch separator. GO is not T-SQL, so this may surface as a parse error instead.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates\nGO\nSELECT TOP 10 Metric FROM dbo.Certificates")]
    public void Rule2_Multiple_statements_are_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.MultipleStatements, result.Reason);
    }

    [Theory]
    [InlineData("-- just a comment")]
    [InlineData("/* just a comment */")]
    [InlineData(";")]
    public void Rule2_Input_without_a_statement_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.NoStatement, result.Reason);
    }

    // Rule 3: the statement must be a SELECT.

    [Theory]
    [InlineData("select top 10 equipmentname from dbo.certificates")]
    [InlineData("SELECT TOP 10 [EquipmentName] FROM [dbo].[Certificates]")]
    // Tricky: starts with WITH but the statement is a SELECT.
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates) SELECT TOP 10 EquipmentName FROM c")]
    // Tricky: UNION is still a SELECT statement.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 Metric FROM dbo.Certificates")]
    public void Rule3_Select_statement_is_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Certificates (EquipmentName) VALUES ('x')")]
    [InlineData("UPDATE dbo.Certificates SET IsCompliant = 1")]
    [InlineData("DELETE FROM dbo.Certificates")]
    [InlineData("MERGE dbo.Certificates AS t USING dbo.Certificates AS s ON t.Id = s.Id WHEN MATCHED THEN DELETE;")]
    [InlineData("DROP TABLE dbo.Certificates")]
    [InlineData("TRUNCATE TABLE dbo.Certificates")]
    [InlineData("EXEC sp_who")]
    [InlineData("DECLARE @x int")]
    [InlineData("SET NOCOUNT ON")]
    // Tricky: starts with WITH but the statement is a DELETE.
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates) DELETE FROM c")]
    // Tricky: a leading comment containing SELECT must not make it look like a SELECT.
    [InlineData("/* SELECT */ DELETE FROM dbo.Certificates")]
    // Tricky: a SELECT nested in a block is not a top-level SelectStatement.
    [InlineData("BEGIN SELECT TOP 10 EquipmentName FROM dbo.Certificates END")]
    // Tricky: a destructive statement nested in a conditional.
    [InlineData("IF 1=1 DELETE FROM dbo.Certificates")]
    public void Rule3_Non_select_statement_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.NotSelect, result.Reason);
    }
}
