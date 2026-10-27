using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 7: no SELECT INTO, no FOR clause, no OPTION or table hint, and no function outside the allowlist,
/// at any depth (subqueries, CTEs, derived tables, UNION branches).
/// OPENROWSET and OPENQUERY are table sources, so rule 4 rejects them before this runs.
/// </summary>
internal static class ForbiddenConstructChecker
{
    // CAST, CONVERT, TRY_CAST, TRY_CONVERT and CURRENT_TIMESTAMP are separate AST nodes, see IsForbiddenFunction.
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT",
        "MIN",
        "MAX",
        "GETDATE",
        "SYSDATETIME",
        "DATEADD",
        "DATEDIFF",
        "YEAR",
        "MONTH",
        "DAY",
        "LOWER",
        "UPPER",
    };

    /// <returns>The rejection reason, or null when no forbidden construct is used.</returns>
    public static string? FindViolation(SelectStatement select)
    {
        if (select.Into is not null)
        {
            return SqlGuardReasons.SelectIntoNotAllowed;
        }

        var visitor = new ConstructVisitor();
        select.Accept(visitor);
        return visitor.Reason;
    }

    // CASE, COALESCE and NULLIF are expressions, not function calls, so they are not listed here and stay allowed.
    // Anything function-like that is not a plain FunctionCall node is listed explicitly, so it fails closed.
    private static bool IsForbiddenFunction(TSqlFragment node) => node switch
    {
        FunctionCall call => call.CallTarget is not null
            || call.OverClause is not null
            || !AllowedFunctions.Contains(call.FunctionName.Value),
        ParameterlessCall call => call.ParameterlessCallType != ParameterlessCallType.CurrentTimestamp,
        GlobalVariableExpression
            or IIfCall
            or ParseCall
            or TryParseCall
            or LeftFunctionCall
            or RightFunctionCall
            or OdbcFunctionCall
            or NextValueForExpression => true,
        _ => false,
    };

    private sealed class ConstructVisitor : TSqlFragmentVisitor
    {
        public string? Reason { get; private set; }

        // Called for every fragment, so the first violation in document order wins.
        public override void Visit(TSqlFragment node)
        {
            Reason ??= node switch
            {
                ForClause => SqlGuardReasons.ForClauseNotAllowed,
                OptimizerHint or TableHint => SqlGuardReasons.QueryHintNotAllowed,
                _ when IsForbiddenFunction(node) => SqlGuardReasons.FunctionNotAllowed,
                _ => null,
            };
        }
    }
}
