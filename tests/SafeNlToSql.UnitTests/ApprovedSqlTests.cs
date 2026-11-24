using System.Reflection;
using SafeNlToSql.Application;

namespace SafeNlToSql.UnitTests;

public class ApprovedSqlTests
{
    private readonly SqlGuard _guard = new();

    [Fact]
    public void Allowed_result_carries_ApprovedSql_with_original_text()
    {
        // Leading and trailing whitespace and a comment: the text must come back untouched, never re-scripted.
        const string sql = "  SELECT TOP 10 EquipmentName FROM dbo.Certificates -- note\n";

        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.NotNull(result.Approved);
        Assert.Equal(sql, result.Approved.Sql);
    }

    [Theory]
    [InlineData("DROP TABLE dbo.Certificates")]
    [InlineData("SELECT TOP 10 ClientName FROM dbo.Certificates")]
    [InlineData("SELECT EquipmentName FROM dbo.Certificates")]
    [InlineData("")]
    public void Rejected_result_has_no_ApprovedSql(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Approved);
    }

    [Fact]
    public void ApprovedSql_has_no_public_constructor()
    {
        var publicConstructors = typeof(ApprovedSql).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Only_SqlGuard_results_can_carry_ApprovedSql()
    {
        // Outside the Application assembly the only GuardResult a caller can build is a rejection:
        // there is no public constructor, the Approved setter is not public, and the Allow factory is internal.
        var constructors = typeof(GuardResult).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(constructors);

        var setter = typeof(GuardResult).GetProperty(nameof(GuardResult.Approved))!.SetMethod;
        Assert.True(setter is null || !setter.IsPublic);

        var allow = typeof(GuardResult).GetMethod("Allow", BindingFlags.Public | BindingFlags.Static);
        Assert.Null(allow);

        var rejected = GuardResult.Reject("because");
        Assert.False(rejected.IsAllowed);
        Assert.Null(rejected.Approved);
        Assert.Equal("because", rejected.Reason);
    }

    [Fact]
    public void IsAllowed_is_true_exactly_when_Approved_is_set()
    {
        var allowed = _guard.Validate("SELECT TOP 10 EquipmentName FROM dbo.Certificates");
        var rejected = _guard.Validate("DROP TABLE dbo.Certificates");

        Assert.Equal(allowed.Approved is not null, allowed.IsAllowed);
        Assert.True(allowed.IsAllowed);
        Assert.Equal(rejected.Approved is not null, rejected.IsAllowed);
        Assert.False(rejected.IsAllowed);
    }
}
