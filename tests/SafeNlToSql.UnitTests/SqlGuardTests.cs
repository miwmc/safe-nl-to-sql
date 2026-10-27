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

    // Rule 5: only allowlisted columns may be referenced, anywhere. SELECT * is rejected.
    // The allowlist is case-insensitive. A column reference must be an allowlisted name itself:
    // references to aliases are rejected, output aliases (AS x) are fine.
    // Accepted cases already satisfy the other rules (dbo.Certificates, TOP <= 100, allowlisted functions).

    [Theory]
    [InlineData("SELECT TOP 10 CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant FROM dbo.Certificates")]
    // Casing and brackets.
    [InlineData("SELECT TOP 10 EQUIPMENTNAME, [metric], iscompliant FROM dbo.Certificates")]
    // Qualified by alias, by table, by schema and table.
    [InlineData("SELECT TOP 10 c.EquipmentName FROM dbo.Certificates c WHERE c.IsCompliant = 0 ORDER BY c.CalibrationDate")]
    [InlineData("SELECT TOP 10 dbo.Certificates.EquipmentName FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentType, COUNT(*) FROM dbo.Certificates GROUP BY EquipmentType HAVING COUNT(*) > 1")]
    [InlineData("SELECT TOP 10 EquipmentName AS Name FROM dbo.Certificates")]
    // Tricky: COUNT(*) is not SELECT *.
    [InlineData("SELECT TOP 1 COUNT(*) FROM dbo.Certificates")]
    // Tricky: the multiplication operator is not a star expression.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE 2 * 3 = 6")]
    // Tricky: a forbidden name in a string literal or a comment is not a column reference.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentName = 'ClientName'")]
    [InlineData("SELECT TOP 10 EquipmentName /* ClientName */ FROM dbo.Certificates -- ClientName")]
    // Tricky: an output alias may have any name, it is not a column reference.
    [InlineData("SELECT TOP 10 EquipmentName AS ClientName FROM dbo.Certificates")]
    public void Rule5_Allowed_columns_are_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    // ClientName in every spelling.
    [InlineData("SELECT TOP 10 ClientName FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 [ClientName] FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 [clientname] FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 CLIENTNAME FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 c.ClientName FROM dbo.Certificates c")]
    [InlineData("SELECT TOP 10 dbo.Certificates.ClientName FROM dbo.Certificates")]
    // ClientName in every clause, not just the select list.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE ClientName = 'Acme'")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates ORDER BY ClientName")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates GROUP BY EquipmentName, ClientName")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates GROUP BY EquipmentName HAVING MAX(ClientName) = 'Acme'")]
    [InlineData("SELECT TOP 10 a.EquipmentName FROM dbo.Certificates a JOIN dbo.Certificates b ON a.ClientName = b.ClientName")]
    [InlineData("SELECT TOP 1 COUNT(ClientName) FROM dbo.Certificates")]
    // ClientName inside subqueries, derived tables and UNION branches.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentType IN (SELECT ClientName FROM dbo.Certificates)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EXISTS (SELECT 1 FROM dbo.Certificates x WHERE x.ClientName = 'Acme')")]
    [InlineData("SELECT TOP 10 d.ClientName FROM (SELECT ClientName FROM dbo.Certificates) AS d")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 ClientName FROM dbo.Certificates")]
    // Columns outside the allowlist that are not secret are still rejected.
    [InlineData("SELECT TOP 10 Id FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates ORDER BY Id")]
    [InlineData("SELECT TOP 10 NoSuchColumn FROM dbo.Certificates")]
    // Pseudo-columns expose Id and other hidden data.
    [InlineData("SELECT TOP 10 $IDENTITY FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 $ROWGUID FROM dbo.Certificates")]
    // Tricky: ClientName aliased to an allowed name inside a CTE. The reference inside the CTE is what is caught.
    [InlineData("WITH c AS (SELECT ClientName AS EquipmentName FROM dbo.Certificates) SELECT TOP 10 EquipmentName FROM c")]
    // Strict on aliases: a reference to an output alias is not an allowlisted name.
    [InlineData("SELECT TOP 10 EquipmentName AS Name FROM dbo.Certificates ORDER BY Name")]
    [InlineData("SELECT TOP 10 d.Name FROM (SELECT EquipmentName AS Name FROM dbo.Certificates) AS d")]
    [InlineData("WITH c (Name) AS (SELECT EquipmentName FROM dbo.Certificates) SELECT TOP 10 Name FROM c")]
    // Tricky: an alias named ClientName is still not a column the caller may reference.
    [InlineData("SELECT TOP 10 EquipmentName AS ClientName FROM dbo.Certificates ORDER BY ClientName")]
    public void Rule5_Other_columns_are_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.ColumnNotAllowed, result.Reason);
    }

    [Theory]
    [InlineData("SELECT TOP 10 * FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 c.* FROM dbo.Certificates c")]
    [InlineData("SELECT TOP 10 dbo.Certificates.* FROM dbo.Certificates")]
    // Tricky: star inside a subquery, even where it is harmless in SQL Server (EXISTS).
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EXISTS (SELECT * FROM dbo.Certificates)")]
    // Tricky: star inside a derived table or CTE still returns ClientName to the outer query.
    [InlineData("SELECT TOP 10 d.EquipmentName FROM (SELECT * FROM dbo.Certificates) AS d")]
    [InlineData("WITH c AS (SELECT * FROM dbo.Certificates) SELECT TOP 10 EquipmentName FROM c")]
    // Tricky: star in the second UNION branch.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 * FROM dbo.Certificates")]
    public void Rule5_Select_star_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.SelectStarNotAllowed, result.Reason);
    }

    // Rule 6: date filters only on CalibrationDate.
    // A date-valued expression is a date function (GETDATE, DATEADD, YEAR, ...), CURRENT_TIMESTAMP or a
    // CAST/CONVERT to a date type. Two things are enforced:
    //  - a column inside a date-valued expression must be CalibrationDate;
    //  - a comparison, BETWEEN, IN or LIKE that contains a date-valued expression may only involve CalibrationDate.
    // A text column compared with a date-like string is not a date filter, so it stays allowed.
    // CalibrationDate is the only date column, so this mostly guards against nonsense comparisons.
    // Accepted cases already satisfy the other rules (dbo.Certificates, allowlisted columns, TOP <= 100, allowlisted functions).

    private const string DatePrefix = "SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE ";

    [Theory]
    [InlineData(DatePrefix + "CalibrationDate >= DATEADD(day, -30, GETDATE())")]
    [InlineData(DatePrefix + "CalibrationDate > CURRENT_TIMESTAMP")]
    [InlineData(DatePrefix + "CalibrationDate < SYSDATETIME()")]
    [InlineData(DatePrefix + "CAST(CalibrationDate AS date) = CAST(GETDATE() AS date)")]
    [InlineData(DatePrefix + "CONVERT(date, CalibrationDate) < GETDATE()")]
    [InlineData(DatePrefix + "CalibrationDate BETWEEN DATEADD(month, -6, GETDATE()) AND GETDATE()")]
    [InlineData(DatePrefix + "YEAR(CalibrationDate) = 2024")]
    [InlineData(DatePrefix + "DATEDIFF(day, CalibrationDate, GETDATE()) < 30")]
    [InlineData(DatePrefix + "CalibrationDate = '2024-05-01'")]
    [InlineData("SELECT TOP 10 YEAR(CalibrationDate) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 DATEADD(day, 1, CalibrationDate) FROM dbo.Certificates")]
    // Tricky: a text column compared with a date-like string is not a date filter.
    [InlineData(DatePrefix + "CertificateNumber = '2024-05-01'")]
    [InlineData(DatePrefix + "CertificateNumber LIKE '2024-%'")]
    // Tricky: other predicates in the same WHERE are checked on their own, not as part of the date comparison.
    [InlineData(DatePrefix + "EquipmentType = 'Fridge' AND CalibrationDate >= DATEADD(day, -30, GETDATE())")]
    [InlineData(DatePrefix + "IsCompliant = 1 AND CalibrationDate < SYSDATETIME()")]
    // Tricky: CONVERT to a text type is not date-valued, so the text comparison is not a date filter.
    [InlineData(DatePrefix + "CertificateNumber = CONVERT(varchar(10), CalibrationDate, 23)")]
    // Tricky: a date comparison inside a subquery does not drag the outer IN column into the check.
    [InlineData(DatePrefix + "EquipmentType IN (SELECT EquipmentType FROM dbo.Certificates WHERE CalibrationDate > GETDATE())")]
    public void Rule6_Date_filter_on_calibration_date_is_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    // A date function on another column, in any clause.
    [InlineData("SELECT TOP 10 YEAR(EquipmentName) FROM dbo.Certificates")]
    [InlineData(DatePrefix + "MONTH(Metric) = 1")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates ORDER BY DAY(CertificateNumber)")]
    [InlineData(DatePrefix + "DATEDIFF(day, EquipmentName, GETDATE()) < 30")]
    [InlineData("SELECT TOP 10 DATEADD(day, 1, EquipmentName) FROM dbo.Certificates")]
    // CAST and CONVERT to a date type on another column.
    [InlineData(DatePrefix + "CAST(CertificateNumber AS date) > GETDATE()")]
    [InlineData(DatePrefix + "CONVERT(date, EquipmentName) > GETDATE()")]
    [InlineData(DatePrefix + "TRY_CAST(CertificateNumber AS date) > GETDATE()")]
    [InlineData(DatePrefix + "CAST(Metric AS datetime2(3)) < GETDATE()")]
    // A date-valued expression compared with another column.
    [InlineData(DatePrefix + "EquipmentName = GETDATE()")]
    [InlineData(DatePrefix + "EquipmentType > DATEADD(year, -1, GETDATE())")]
    [InlineData(DatePrefix + "Metric = CURRENT_TIMESTAMP")]
    [InlineData(DatePrefix + "EquipmentName BETWEEN GETDATE() AND DATEADD(year, 1, GETDATE())")]
    [InlineData(DatePrefix + "CertificateNumber IN (GETDATE(), CURRENT_TIMESTAMP)")]
    [InlineData(DatePrefix + "YEAR(CalibrationDate) = CertificateNumber")]
    // LIKE counts as a comparison.
    [InlineData(DatePrefix + "EquipmentName LIKE GETDATE()")]
    [InlineData(DatePrefix + "CAST(CalibrationDate AS date) LIKE EquipmentName")]
    // Tricky: the wrong column is nested inside a comparison that also uses CalibrationDate.
    [InlineData(DatePrefix + "CalibrationDate = DATEADD(day, 1, EquipmentName)")]
    // Tricky: an aggregate over another column compared with a date.
    [InlineData("SELECT TOP 10 EquipmentType FROM dbo.Certificates GROUP BY EquipmentType HAVING MIN(EquipmentName) = GETDATE()")]
    // Tricky: function names are case-insensitive.
    [InlineData(DatePrefix + "EquipmentName = getdate()")]
    // Tricky: the same violation inside a subquery, a CTE and a UNION branch.
    [InlineData(DatePrefix + "EquipmentType IN (SELECT EquipmentType FROM dbo.Certificates WHERE EquipmentName = GETDATE())")]
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates WHERE EquipmentName > GETDATE()) SELECT TOP 10 EquipmentName FROM c")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentName = GETDATE()")]
    public void Rule6_Date_filter_on_other_columns_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.DateFilterNotAllowed, result.Reason);
    }

    // Rule 7: no SELECT INTO, no FOR clause, no OPTION hint, and only allowlisted functions.
    // Function allowlist (case-insensitive): COUNT, MIN, MAX, GETDATE, SYSDATETIME, DATEADD, DATEDIFF, YEAR, MONTH,
    // DAY, LOWER, UPPER, CAST, CONVERT, TRY_CAST, TRY_CONVERT and CURRENT_TIMESTAMP. Everything function-like
    // that is not on it is rejected: schema-qualified calls, @@ globals, anything with an OVER clause.
    // Checked at every depth. Accepted cases already satisfy the other rules.

    [Theory]
    [InlineData("SELECT TOP 10 UPPER(EquipmentName), LOWER(EquipmentType) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentType, COUNT(*), MIN(CalibrationDate), MAX(CalibrationDate) FROM dbo.Certificates GROUP BY EquipmentType")]
    [InlineData("SELECT TOP 1 COUNT(DISTINCT EquipmentType) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 CAST(IsCompliant AS int), TRY_CAST(IsCompliant AS int), TRY_CONVERT(int, IsCompliant), CONVERT(varchar(10), CalibrationDate, 23) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 YEAR(CalibrationDate), MONTH(CalibrationDate), DAY(CalibrationDate) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE CalibrationDate >= DATEADD(day, -30, GETDATE()) AND CalibrationDate < SYSDATETIME() AND DATEDIFF(day, CalibrationDate, CURRENT_TIMESTAMP) > 1")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE LOWER(EquipmentType) = 'fridge'")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates ORDER BY UPPER(EquipmentName)")]
    // Tricky: function names are case-insensitive.
    [InlineData("select top 10 upper(equipmentname) from dbo.certificates")]
    [InlineData("SELECT TOP 1 Count(*) FROM dbo.Certificates")]
    // Tricky: allowed functions at depth.
    [InlineData("SELECT TOP 10 UPPER(EquipmentName) FROM dbo.Certificates UNION SELECT TOP 10 LOWER(EquipmentType) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentType IN (SELECT LOWER(EquipmentType) FROM dbo.Certificates)")]
    // Tricky: forbidden names in a string literal or a comment are not function calls or clauses.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentName = 'DB_NAME() FOR XML OPTION (RECOMPILE) INTO #t' -- LEN(x) OPTION (MAXDOP 1)")]
    public void Rule7_Allowed_functions_are_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    // CASE, COALESCE and NULLIF are expressions, not function calls, and read nothing new.
    [Theory]
    [InlineData("SELECT TOP 10 CASE WHEN IsCompliant = 1 THEN 'yes' ELSE 'no' END FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 COALESCE(Metric, EquipmentType) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 NULLIF(Metric, EquipmentType) FROM dbo.Certificates")]
    public void Rule7_Expressions_that_are_not_function_calls_are_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Theory]
    // Functions that are not on the allowlist.
    [InlineData("SELECT TOP 10 LEN(EquipmentName) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 SUBSTRING(EquipmentName, 1, 3) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 CONCAT(EquipmentName, EquipmentType) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 ISNULL(Metric, 'x') FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 STRING_AGG(EquipmentName, ',') FROM dbo.Certificates")]
    // System information.
    [InlineData("SELECT TOP 10 DB_NAME() FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 SERVERPROPERTY('ProductVersion') FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 SUSER_SNAME() FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 CURRENT_USER FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 SYSTEM_USER FROM dbo.Certificates")]
    // Function-like syntax that is not a plain FunctionCall node.
    [InlineData("SELECT TOP 10 LEFT(EquipmentName, 3) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 IIF(IsCompliant = 1, 'yes', 'no') FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 PARSE(CertificateNumber AS int) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 {fn UCASE(EquipmentName)} FROM dbo.Certificates")]
    // NEXT VALUE FOR advances a sequence, which is a write.
    [InlineData("SELECT TOP 10 NEXT VALUE FOR dbo.CertSeq FROM dbo.Certificates")]
    // Schema-qualified calls, even when the function name is on the allowlist.
    [InlineData("SELECT TOP 10 dbo.GETDATE() FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 [dbo].[GETDATE]() FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 master.dbo.GETDATE() FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 sys.fn_varbintohexstr(0x00) FROM dbo.Certificates")]
    // @@ global variables.
    [InlineData("SELECT TOP 10 @@VERSION FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentName = @@SERVERNAME")]
    // OVER clause, even on allowlisted aggregates.
    [InlineData("SELECT TOP 10 COUNT(*) OVER () FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 MIN(CalibrationDate) OVER (PARTITION BY EquipmentType) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 ROW_NUMBER() OVER (ORDER BY CalibrationDate) FROM dbo.Certificates")]
    // Every clause.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE LEN(EquipmentName) > 3")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates ORDER BY LEN(EquipmentName)")]
    [InlineData("SELECT TOP 10 EquipmentType FROM dbo.Certificates GROUP BY EquipmentType HAVING LEN(EquipmentType) > 3")]
    [InlineData("SELECT TOP 10 a.EquipmentName FROM dbo.Certificates a JOIN dbo.Certificates b ON LEN(a.EquipmentName) = LEN(b.EquipmentName)")]
    // Every depth: subquery, scalar subquery, derived table, CTE, UNION branch.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentType IN (SELECT LEN(EquipmentType) FROM dbo.Certificates)")]
    [InlineData("SELECT TOP 10 (SELECT TOP 1 LEN(EquipmentName) FROM dbo.Certificates) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 d.EquipmentName FROM (SELECT EquipmentName FROM dbo.Certificates WHERE LEN(EquipmentName) > 3) AS d")]
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates WHERE LEN(EquipmentName) > 3) SELECT TOP 10 EquipmentName FROM c")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE LEN(EquipmentName) > 3")]
    // Tricky: a forbidden function nested inside an allowed one.
    [InlineData("SELECT TOP 10 UPPER(REPLACE(EquipmentName, 'a', 'b')) FROM dbo.Certificates")]
    // Tricky: casing and brackets do not change the function name.
    [InlineData("SELECT TOP 10 len(EquipmentName) FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 [LEN](EquipmentName) FROM dbo.Certificates")]
    // Tricky: a name that starts with an allowed one is a different function.
    [InlineData("SELECT TOP 1 COUNT_BIG(*) FROM dbo.Certificates")]
    public void Rule7_Other_functions_are_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.FunctionNotAllowed, result.Reason);
    }

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName INTO #t FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName INTO ##t FROM dbo.Certificates")]
    [InlineData("SELECT TOP 10 EquipmentName INTO dbo.Copy FROM dbo.Certificates")]
    // Tricky: INTO in the first branch of a UNION, and after a CTE.
    [InlineData("SELECT TOP 10 EquipmentName INTO #t FROM dbo.Certificates UNION SELECT TOP 10 EquipmentName FROM dbo.Certificates")]
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates) SELECT TOP 10 EquipmentName INTO #t FROM c")]
    public void Rule7_Select_into_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.SelectIntoNotAllowed, result.Reason);
    }

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates FOR XML AUTO")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates FOR XML PATH('')")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates FOR JSON AUTO")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates FOR JSON PATH")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates FOR BROWSE")]
    // Tricky: after ORDER BY.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates ORDER BY EquipmentName FOR JSON AUTO")]
    // Tricky: at depth, in a scalar subquery and in a CTE.
    [InlineData("SELECT TOP 10 (SELECT TOP 1 EquipmentName FROM dbo.Certificates FOR XML PATH('')) FROM dbo.Certificates")]
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates FOR XML PATH('')) SELECT TOP 10 EquipmentName FROM c")]
    // Tricky: on the last branch of a UNION.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 EquipmentType FROM dbo.Certificates FOR XML AUTO")]
    public void Rule7_For_clause_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.ForClauseNotAllowed, result.Reason);
    }

    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates OPTION (MAXDOP 1)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates OPTION (RECOMPILE)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates OPTION (MAXRECURSION 0)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates OPTION (HASH JOIN)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates OPTION (OPTIMIZE FOR UNKNOWN)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates OPTION (LABEL = 'x')")]
    // Tricky: after ORDER BY, after a CTE, after a UNION.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates ORDER BY EquipmentName OPTION (RECOMPILE)")]
    [InlineData("WITH c AS (SELECT EquipmentName FROM dbo.Certificates) SELECT TOP 10 EquipmentName FROM c OPTION (RECOMPILE)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates UNION SELECT TOP 10 EquipmentType FROM dbo.Certificates OPTION (RECOMPILE)")]
    // Table hints are rejected too, whatever the hint.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WITH (NOLOCK)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WITH (UPDLOCK, HOLDLOCK)")]
    // Tricky: an index hint on one side of a JOIN.
    [InlineData("SELECT TOP 10 a.EquipmentName FROM dbo.Certificates a WITH (INDEX(0)) JOIN dbo.Certificates b ON a.CertificateNumber = b.CertificateNumber")]
    // Tricky: a table hint inside a subquery.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentType IN (SELECT EquipmentType FROM dbo.Certificates WITH (NOLOCK))")]
    // Tricky: the old table hint syntax, without WITH.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates (NOLOCK)")]
    public void Rule7_Query_hint_is_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.QueryHintNotAllowed, result.Reason);
    }

    // One case per expression and predicate type that is on the allowlist but not covered by the cases above.
    [Theory]
    // ParenthesisExpression, and the TOP (n) form.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE (2 + 3) * 2 = 10")]
    [InlineData("SELECT TOP (10) EquipmentName FROM dbo.Certificates")]
    // BooleanParenthesisExpression
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE (EquipmentType = 'Fridge' OR EquipmentType = 'Microwave') AND IsCompliant = 0")]
    // BooleanNotExpression
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE NOT EquipmentType = 'Fridge'")]
    // BooleanIsNullExpression
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE Metric IS NOT NULL")]
    // ExistsPredicate
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EXISTS (SELECT 1 FROM dbo.Certificates WHERE IsCompliant = 0)")]
    // SimpleCaseExpression
    [InlineData("SELECT TOP 10 CASE EquipmentType WHEN 'Fridge' THEN 1 ELSE 0 END FROM dbo.Certificates")]
    // NullLiteral
    [InlineData("SELECT TOP 10 COALESCE(Metric, NULL) FROM dbo.Certificates")]
    // NumericLiteral
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE 2.5 > 1.5")]
    // MaxLiteral
    [InlineData("SELECT TOP 10 CAST(EquipmentName AS varchar(max)) FROM dbo.Certificates")]
    public void Rule7_Allowlisted_expression_types_are_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    // Fail closed: an expression or predicate type that is not on the allowlist is rejected, even though
    // no rule names it. Full-text predicates are predicates, not function calls.
    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE CONTAINS(EquipmentName, 'x')")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE FREETEXT(EquipmentName, 'x')")]
    // Variables are not on the allowlist.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentName = @x")]
    [InlineData("SELECT TOP 10 CalibrationDate AT TIME ZONE 'UTC' FROM dbo.Certificates")]
    // Tricky: ANY/SOME/ALL subquery comparisons are not plain comparisons.
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE EquipmentType = ANY (SELECT EquipmentType FROM dbo.Certificates)")]
    [InlineData("SELECT TOP 10 EquipmentName FROM dbo.Certificates WHERE Metric IS DISTINCT FROM EquipmentType")]
    public void Rule7_Constructs_outside_the_allowlist_are_rejected(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.ExpressionNotAllowed, result.Reason);
    }

    // OPENROWSET and OPENQUERY are table sources, so rule 4 rejects them before rule 7 runs.
    [Theory]
    [InlineData("SELECT TOP 10 EquipmentName FROM OPENROWSET('SQLNCLI', 'Server=x;Trusted_Connection=yes;', 'SELECT 1')")]
    [InlineData("SELECT TOP 10 EquipmentName FROM OPENQUERY(linked, 'SELECT 1')")]
    public void Rule7_Openrowset_and_openquery_stay_table_not_allowed(string sql)
    {
        var result = _guard.Validate(sql);

        Assert.False(result.IsAllowed);
        Assert.Equal(SqlGuardReasons.TableNotAllowed, result.Reason);
    }
}
