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
        parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0)
        {
            return GuardResult.Reject(SqlGuardReasons.NotParseable);
        }

        return GuardResult.Allow();
    }
}
