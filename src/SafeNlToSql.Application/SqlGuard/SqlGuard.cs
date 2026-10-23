using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

public sealed class SqlGuard
{
    public GuardResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return GuardResult.Reject(SqlGuardReasons.Empty);
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0 || fragment is not TSqlScript script)
        {
            return GuardResult.Reject(SqlGuardReasons.NotParseable);
        }

        var statementCount = script.Batches.Sum(batch => batch.Statements.Count);
        if (statementCount == 0)
        {
            return GuardResult.Reject(SqlGuardReasons.NoStatement);
        }

        if (statementCount > 1)
        {
            return GuardResult.Reject(SqlGuardReasons.MultipleStatements);
        }

        return GuardResult.Allow();
    }
}
