using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 6: date filters only on CalibrationDate. A date-valued expression is a date function,
/// CURRENT_TIMESTAMP or a CAST/CONVERT to a date type. Two checks:
/// the columns inside a date-valued expression must be CalibrationDate, and a comparison, BETWEEN, IN
/// or LIKE that contains one may only involve CalibrationDate. CalibrationDate is the only date column,
/// so this mostly guards against nonsense comparisons.
/// </summary>
internal static class DateFilterChecker
{
    private const string AllowedColumn = "CalibrationDate";

    // YEAR, MONTH, DAY and DATEDIFF return ints but take a date, so they are treated as date functions too.
    private static readonly HashSet<string> DateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GETDATE",
        "SYSDATETIME",
        "DATEADD",
        "DATEDIFF",
        "YEAR",
        "MONTH",
        "DAY",
    };

    private static readonly HashSet<SqlDataTypeOption> DateTypes =
    [
        SqlDataTypeOption.Date,
        SqlDataTypeOption.DateTime,
        SqlDataTypeOption.DateTime2,
        SqlDataTypeOption.SmallDateTime,
        SqlDataTypeOption.DateTimeOffset,
        SqlDataTypeOption.Time,
    ];

    public static bool IsAllowed(SelectStatement select)
    {
        var visitor = new DateVisitor();
        select.Accept(visitor);
        return !visitor.HasViolation;
    }

    private static bool IsDateFunction(FunctionCall node) =>
        node.CallTarget is null && DateFunctions.Contains(node.FunctionName.Value);

    private static bool IsDateType(DataTypeReference? type) =>
        type is SqlDataTypeReference sqlType && DateTypes.Contains(sqlType.SqlDataTypeOption);

    private sealed class DateVisitor : TSqlFragmentVisitor
    {
        public bool HasViolation { get; private set; }

        public override void Visit(FunctionCall node)
        {
            if (IsDateFunction(node))
            {
                CheckColumnsInside(node);
            }
        }

        public override void Visit(CastCall node) => CheckCast(node, node.DataType);

        public override void Visit(TryCastCall node) => CheckCast(node, node.DataType);

        public override void Visit(ConvertCall node) => CheckCast(node, node.DataType);

        public override void Visit(TryConvertCall node) => CheckCast(node, node.DataType);

        public override void Visit(BooleanComparisonExpression node) =>
            CheckPredicate(node.FirstExpression, node.SecondExpression);

        public override void Visit(BooleanTernaryExpression node) =>
            CheckPredicate(node.FirstExpression, node.SecondExpression, node.ThirdExpression);

        public override void Visit(InPredicate node) =>
            CheckPredicate([node.Expression, .. node.Values]);

        public override void Visit(LikePredicate node) =>
            CheckPredicate(node.FirstExpression, node.SecondExpression, node.EscapeExpression);

        private void CheckCast(TSqlFragment node, DataTypeReference? type)
        {
            if (IsDateType(type))
            {
                CheckColumnsInside(node);
            }
        }

        // Nothing is skipped here: a subquery inside a date function would be a column feeding that function.
        private void CheckColumnsInside(TSqlFragment node)
        {
            var scanner = new Scanner(skipSubqueries: false);
            node.Accept(scanner);
            HasViolation |= scanner.HasOtherColumn;
        }

        // Subqueries are skipped: they are separate scopes whose own predicates are visited on their own.
        private void CheckPredicate(params ScalarExpression?[] operands)
        {
            var scanner = new Scanner(skipSubqueries: true);
            foreach (var operand in operands)
            {
                operand?.Accept(scanner);
            }

            HasViolation |= scanner.HasDateExpression && scanner.HasOtherColumn;
        }
    }

    private sealed class Scanner(bool skipSubqueries) : TSqlFragmentVisitor
    {
        public bool HasDateExpression { get; private set; }

        public bool HasOtherColumn { get; private set; }

        public override void ExplicitVisit(ScalarSubquery node)
        {
            if (!skipSubqueries)
            {
                base.ExplicitVisit(node);
            }
        }

        public override void Visit(ColumnReferenceExpression node)
        {
            // Anything but a plain CalibrationDate counts, including * and pseudo-columns.
            var isCalibrationDate = node.ColumnType == ColumnType.Regular
                && node.MultiPartIdentifier?.Identifiers.LastOrDefault() is { } column
                && string.Equals(column.Value, AllowedColumn, StringComparison.OrdinalIgnoreCase);
            HasOtherColumn |= !isCalibrationDate;
        }

        public override void Visit(FunctionCall node) => HasDateExpression |= IsDateFunction(node);

        public override void Visit(ParameterlessCall node) =>
            HasDateExpression |= node.ParameterlessCallType == ParameterlessCallType.CurrentTimestamp;

        public override void Visit(CastCall node) => HasDateExpression |= IsDateType(node.DataType);

        public override void Visit(TryCastCall node) => HasDateExpression |= IsDateType(node.DataType);

        public override void Visit(ConvertCall node) => HasDateExpression |= IsDateType(node.DataType);

        public override void Visit(TryConvertCall node) => HasDateExpression |= IsDateType(node.DataType);
    }
}
