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
}
