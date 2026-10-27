using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

public sealed class SqlGuard
{
    public const int MaxSqlLength = 2000;

    private readonly Func<TextReader, (TSqlFragment? Fragment, IList<ParseError> Errors)> _parse;

    public SqlGuard() : this(DefaultParse)
    {
    }

    internal SqlGuard(Func<TextReader, (TSqlFragment? Fragment, IList<ParseError> Errors)> parse)
    {
        _parse = parse;
    }

    private static (TSqlFragment? Fragment, IList<ParseError> Errors) DefaultParse(TextReader reader)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        return (fragment, errors);
    }

    public GuardResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardResult.Reject(SqlGuardReasons.Empty);
        }

        // Checked before parsing: this bounds nesting depth, which the catch below cannot
        // protect against (a stack overflow is not catchable).
        if (sql.Length > MaxSqlLength)
        {
            return GuardResult.Reject(SqlGuardReasons.TooLong);
        }

        TSqlFragment? fragment;
        IList<ParseError> errors;
        try
        {
            using var reader = new StringReader(sql);
            (fragment, errors) = _parse(reader);
        }
        catch (Exception)
        {
            return GuardResult.Reject(SqlGuardReasons.NotParseable);
        }

        if (errors.Count > 0 || fragment is not TSqlScript script)
        {
            return GuardResult.Reject(SqlGuardReasons.NotParseable);
        }

        var statements = script.Batches.SelectMany(batch => batch.Statements).ToList();
        if (statements.Count == 0)
        {
            return GuardResult.Reject(SqlGuardReasons.NoStatement);
        }

        if (statements.Count > 1)
        {
            return GuardResult.Reject(SqlGuardReasons.MultipleStatements);
        }

        if (statements[0] is not SelectStatement select)
        {
            return GuardResult.Reject(SqlGuardReasons.NotSelect);
        }

        var tableViolation = TableReferenceChecker.FindViolation(select);
        if (tableViolation is not null)
        {
            return GuardResult.Reject(tableViolation);
        }

        var columnViolation = ColumnReferenceChecker.FindViolation(select);
        if (columnViolation is not null)
        {
            return GuardResult.Reject(columnViolation);
        }

        if (!DateFilterChecker.IsAllowed(select))
        {
            return GuardResult.Reject(SqlGuardReasons.DateFilterNotAllowed);
        }

        var constructViolation = ForbiddenConstructChecker.FindViolation(select);
        if (constructViolation is not null)
        {
            return GuardResult.Reject(constructViolation);
        }

        var topViolation = TopLimitChecker.FindViolation(select);
        if (topViolation is not null)
        {
            return GuardResult.Reject(topViolation);
        }

        return GuardResult.Allow();
    }
}
