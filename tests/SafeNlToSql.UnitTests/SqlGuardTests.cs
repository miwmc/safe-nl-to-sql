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
}
