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

    // Rule 1: a parser exception counts as unparseable.

    [Fact]
    public void Rule1_Parser_exception_is_rejected_as_not_parseable()
    {
        var guard = new SqlGuard(_ => throw new InvalidOperationException("parser blew up"));

        var result = guard.Validate("SELECT TOP 10 EquipmentName FROM dbo.Certificates");

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.NotParseable, result.Reason);
    }

    // Rule 1: maximum length, checked before parsing so deep nesting never reaches the parser.

    private const string ValidSelect = "SELECT TOP 10 EquipmentName FROM dbo.Certificates";

    public static TheoryData<string> AtOrBelowMaxLength() => new()
    {
        ValidSelect,
        // Boundary: exactly MaxSqlLength characters, padded with trailing spaces.
        ValidSelect + new string(' ', SqlGuard.MaxSqlLength - ValidSelect.Length),
    };

    public static TheoryData<string> AboveMaxLength() => new()
    {
        // Boundary: one character over.
        ValidSelect + new string(' ', SqlGuard.MaxSqlLength - ValidSelect.Length + 1),
        // Tricky: the excess is inside a comment, so it is harmless to the parser but still too long.
        ValidSelect + " /* " + new string('x', SqlGuard.MaxSqlLength) + " */",
        // Tricky: deep nesting must be rejected on length alone, before the parser sees it.
        "SELECT " + new string('(', 5000) + "1" + new string(')', 5000),
    };

    [Theory]
    [MemberData(nameof(AtOrBelowMaxLength))]
    public void Rule1_Sql_up_to_max_length_is_not_rejected_as_too_long(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    [MemberData(nameof(AboveMaxLength))]
    public void Rule1_Sql_above_max_length_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.TooLong, result.Reason);
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

    // Rule 4: only dbo.Certificates may be referenced.
    // Accepted cases already satisfy the later rules (TOP <= 100, allowlisted columns, no functions).

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates")]
    // Schema is optional, brackets are optional, casing does not matter.
    [InlineData("SELECT TOP 10 EquipmentName FROM Certificates")]
    [InlineData("SELECT TOP 10 c.EquipmentName FROM [dbo].[Certificates] AS c")]
    [InlineData("SELECT TOP 10 EquipmentName FROM DBO.CERTIFICATES")]
    [InlineData("SELECT TOP 10 a.EquipmentName FROM dbo.Certificates a JOIN dbo.Certificates b ON a.CertificateNumber = b.CertificateNumber")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentType IN (SELECT EquipmentType FROM dbo.Certificates WHERE IsCompliant = 0)")]
    [InlineData("SELECT TOP 10 d.EquipmentName FROM (SELECT EquipmentName FROM dbo.Certificates) AS d")]
    // Tricky: a CTE name is not a table, and CTE names are case-insensitive.
    [InlineData("WITH C AS (SELECT EquipmentName FROM dbo.Certificates) SELECT TOP 10 EquipmentName FROM c")]
    // Tricky: a CTE that builds on an earlier CTE.
    [InlineData("WITH a AS (SELECT EquipmentName FROM dbo.Certificates), b AS (SELECT EquipmentName FROM a) SELECT TOP 10 EquipmentName FROM b")]
    public void Rule4_Only_certificates_table_is_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    // System views and catalog tables.
    [InlineData("SELECT TOP 10 EquipmentName FROM sys.tables")]
    [InlineData("SELECT TOP 10 EquipmentName FROM INFORMATION_SCHEMA.TABLES")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Equipment")]
    [InlineData("SELECT TOP 10 EquipmentName FROM other.Certificates")]
    // Multi-part names: database and server qualified, even when the table name matches.
    [InlineData("SELECT TOP 10 EquipmentName FROM otherdb.dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM otherdb..Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM srv.otherdb.dbo.Certificates")]
    // Joins.
    [InlineData("SELECT TOP 10 c.CertificateNumber FROM dbo.Certificates c JOIN dbo.Clients x ON c.CertificateNumber = x.CertificateNumber")]
    [InlineData("SELECT TOP 10 c.CertificateNumber FROM dbo.Certificates c, sys.objects o")]
    // Subqueries, derived tables, UNION branches.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentType IN (SELECT EquipmentType FROM sys.tables)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EXISTS (SELECT EquipmentType FROM sys.tables)")]
    [InlineData("SELECT TOP 10 (SELECT EquipmentType FROM sys.tables) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 d.EquipmentName FROM (SELECT EquipmentName FROM sys.tables) AS d")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 EquipmentName FROM sys.tables")]
    // CTEs over other tables, including the second CTE of two.
    [InlineData("WITH c AS (SELECT EquipmentName FROM sys.tables) SELECT TOP 10 EquipmentName FROM c")]
    [InlineData("WITH a AS (SELECT EquipmentName FROM dbo.Certificates), b AS (SELECT EquipmentName FROM sys.tables) SELECT TOP 10 EquipmentName FROM a")]
    // Tricky: bracketed names are still resolved.
    [InlineData("SELECT TOP 10 EquipmentName FROM [sys].[objects]")]
    // Tricky: the real table name in a comment does not count.
    [InlineData("SELECT TOP 10 EquipmentName FROM /* dbo.Certificates */ sys.tables")]
    // Tricky: a CTE named after the allowed table cannot hide the table it wraps.
    [InlineData("WITH Certificates AS (SELECT EquipmentName FROM sys.objects) SELECT TOP 10 EquipmentName FROM Certificates")]
    // Tricky: a CTE name does not make a schema-qualified name allowed (a CTE cannot be schema-qualified).
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates) SELECT TOP 10 EquipmentName FROM dbo.c")]
    // Tricky: a CTE is only visible after its definition. SQL Server resolves the early reference
    // to a real table named Clients, so it must not be mistaken for the CTE defined later.
    [InlineData("WITH a AS (SELECT CertificateNumber FROM Clients), Clients AS (SELECT CertificateNumber FROM dbo.Certificates) SELECT TOP 10 CertificateNumber FROM a")]
    // Tricky: a CTE does not see itself (a recursive reference is not a plain table either).
    [InlineData("WITH Clients AS (SELECT CertificateNumber FROM Clients) SELECT TOP 10 CertificateNumber FROM Clients")]
    // Tricky: a CTE named like a system view does not allow the schema-qualified view.
    [InlineData("WITH tables AS (SELECT EquipmentName FROM dbo.Certificates) SELECT TOP 10 EquipmentName FROM sys.tables")]
    // Fail closed on table sources that are not plain named tables.
    [InlineData("SELECT TOP 10 EquipmentName FROM STRING_SPLIT('a,b', ',')")]
    [InlineData("SELECT TOP 10 EquipmentName FROM OPENROWSET('SQLNCLI', 'Server=x;Trusted_Connection=yes;', 'SELECT 1')")]
    [InlineData("SELECT TOP 10 EquipmentName FROM @t")]
    public void Rule4_Other_tables_are_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.TableNotAllowed, result.Reason);
    }

    // Rule 4: the outer query must read from dbo.Certificates, so a query without FROM is rejected.

    [Theory]
    [InlineData("SELECT TOP 10 1")]
    [InlineData("SELECT TOP 1 GETDATE()")]
    // Tricky: a UNION branch without FROM hides behind a valid first branch.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 1 1")]
    public void Rule4_Query_without_from_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.TableNotAllowed, result.Reason);
    }
}
