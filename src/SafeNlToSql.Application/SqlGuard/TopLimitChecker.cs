using System.Globalization;
using System.Numerics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 8: the outer query, and each branch of a UNION/EXCEPT/INTERSECT, must have TOP with an integer literal
/// from 0 to 100, plain or in parentheses. TOP is never added automatically. Subqueries, derived tables and
/// CTE bodies are not checked: only the query that returns the rows is bounded.
/// </summary>
internal static class TopLimitChecker
{
    private const int MaxTop = 100;

    /// <returns>The rejection reason, or null when every outer branch has an allowed TOP.</returns>
    public static string? FindViolation(SelectStatement select) => Check(select.QueryExpression);

    private static string? Check(QueryExpression query) => query switch
    {
        QuerySpecification spec => CheckTop(spec.TopRowFilter),
        BinaryQueryExpression binary => Check(binary.FirstQueryExpression) ?? Check(binary.SecondQueryExpression),
        QueryParenthesisExpression parenthesis => Check(parenthesis.QueryExpression),
        // A query expression kind we do not know fails closed.
        _ => SqlGuardReasons.TopMissing,
    };

    private static string? CheckTop(TopRowFilter? top)
    {
        // PERCENT and WITH TIES can return more rows than the number says, so they do not count as a bound.
        if (top is null || top.Percent || top.WithTies)
        {
            return SqlGuardReasons.TopMissing;
        }

        var expression = top.Expression;
        while (expression is ParenthesisExpression parenthesis)
        {
            expression = parenthesis.Expression;
        }

        // Anything but a number literal (variable, arithmetic, subquery) cannot be bounded by the guard.
        // ScriptDom gives an IntegerLiteral only up to int.MaxValue; a bigger digits-only value is a NumericLiteral.
        if (expression is not (IntegerLiteral or NumericLiteral))
        {
            return SqlGuardReasons.TopMissing;
        }

        // Digits only: this also rejects a NumericLiteral with a decimal point (TOP 10.5), which is not an integer.
        // BigInteger so that a value beyond int or long is "too large" instead of an exception.
        var text = ((Literal)expression).Value;
        if (!BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return SqlGuardReasons.TopMissing;
        }

        return value <= MaxTop ? null : SqlGuardReasons.TopTooLarge;
    }
}
