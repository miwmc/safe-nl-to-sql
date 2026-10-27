using System.Globalization;
using System.Numerics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 8: the outer query, and each branch of a UNION/EXCEPT/INTERSECT, must have TOP with an integer literal
/// from 0 to 100, plain or in parentheses, and the values of all those branches add up to at most 100, so a
/// UNION cannot multiply the cap. TOP is never added automatically. Subqueries, derived tables and
/// CTE bodies are not checked: only the query that returns the rows is bounded.
/// </summary>
internal static class TopLimitChecker
{
    private const int MaxTop = 100;

    /// <returns>The rejection reason, or null when every outer branch has an allowed TOP and they add up to at most 100.</returns>
    public static string? FindViolation(SelectStatement select)
    {
        var total = BigInteger.Zero;
        var violation = Check(select.QueryExpression, ref total);
        if (violation is not null)
        {
            return violation;
        }

        return total <= MaxTop ? null : SqlGuardReasons.TopTooLarge;
    }

    private static string? Check(QueryExpression query, ref BigInteger total)
    {
        switch (query)
        {
            case QuerySpecification spec:
                return CheckTop(spec.TopRowFilter, ref total);
            case BinaryQueryExpression binary:
                return Check(binary.FirstQueryExpression, ref total) ?? Check(binary.SecondQueryExpression, ref total);
            case QueryParenthesisExpression parenthesis:
                return Check(parenthesis.QueryExpression, ref total);
            // A query expression kind we do not know fails closed.
            default:
                return SqlGuardReasons.TopMissing;
        }
    }

    private static string? CheckTop(TopRowFilter? top, ref BigInteger total)
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

        total += value;
        return value <= MaxTop ? null : SqlGuardReasons.TopTooLarge;
    }
}
