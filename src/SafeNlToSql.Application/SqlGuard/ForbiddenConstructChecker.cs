using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 7: no SELECT INTO, no FOR clause, no OPTION or table hint, and no function outside the allowlist,
/// at any depth (subqueries, CTEs, derived tables, UNION branches).
/// Expressions and predicates fail closed: every ScalarExpression and BooleanExpression node must be of a type
/// on <see cref="AllowedExpressionTypes"/>, so a construct nobody thought of is rejected instead of passed.
/// Table sources are covered by rule 4 (OPENROWSET and OPENQUERY are rejected there before this runs),
/// TOP and OFFSET by rule 8.
/// </summary>
internal static class ForbiddenConstructChecker
{
    // Matched by exact type, so a subclass added by a newer ScriptDom is not allowed implicitly.
    // FunctionCall and ParameterlessCall are further restricted by IsForbiddenFunction.
    private static readonly HashSet<Type> AllowedExpressionTypes =
    [
        // Values
        typeof(ColumnReferenceExpression),
        typeof(StringLiteral),
        typeof(IntegerLiteral),
        typeof(NumericLiteral),
        typeof(NullLiteral),
        typeof(MaxLiteral),
        // The datepart in DATEADD(day, ...)
        typeof(IdentifierLiteral),
        // Operators and grouping
        typeof(BinaryExpression),
        typeof(UnaryExpression),
        typeof(ParenthesisExpression),
        typeof(ScalarSubquery),
        // Calls
        typeof(FunctionCall),
        typeof(ParameterlessCall),
        typeof(CastCall),
        typeof(TryCastCall),
        typeof(ConvertCall),
        typeof(TryConvertCall),
        // CASE, COALESCE and NULLIF are expressions, not function calls
        typeof(SearchedCaseExpression),
        typeof(SimpleCaseExpression),
        typeof(CoalesceExpression),
        typeof(NullIfExpression),
        // Predicates
        typeof(BooleanComparisonExpression),
        typeof(BooleanBinaryExpression),
        typeof(BooleanNotExpression),
        typeof(BooleanParenthesisExpression),
        typeof(BooleanIsNullExpression),
        typeof(BooleanTernaryExpression),
        typeof(InPredicate),
        typeof(LikePredicate),
        typeof(ExistsPredicate),
    ];

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

    // Function-like nodes keep their own reason. Anything else that is not allowlisted gets ExpressionNotAllowed.
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
                ScalarExpression or BooleanExpression when !AllowedExpressionTypes.Contains(node.GetType())
                    => SqlGuardReasons.ExpressionNotAllowed,
                _ => null,
            };
        }
    }
}
