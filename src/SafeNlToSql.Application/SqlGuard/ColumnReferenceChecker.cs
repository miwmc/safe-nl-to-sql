using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 5: every column reference, in any clause and at any depth (CTEs, subqueries, derived tables,
/// UNION branches), must be an allowlisted column. SELECT * is rejected. References to aliases are
/// rejected too: only the real column names are accepted, so an alias cannot hide ClientName.
/// </summary>
internal static class ColumnReferenceChecker
{
    // Matches the case-insensitive default collation of SQL Server.
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "CertificateNumber",
        "EquipmentName",
        "EquipmentType",
        "Metric",
        "CalibrationDate",
        "IsCompliant",
    };

    /// <returns>The rejection reason, or null when every column reference is allowed.</returns>
    public static string? FindViolation(SelectStatement select)
    {
        var visitor = new ColumnVisitor();
        select.Accept(visitor);
        return visitor.Reason;
    }

    private sealed class ColumnVisitor : TSqlFragmentVisitor
    {
        public string? Reason { get; private set; }

        public override void Visit(ColumnReferenceExpression node)
        {
            switch (node.ColumnType)
            {
                // The * in COUNT(*). It is not SELECT *; rule 7 restricts which functions may take it.
                case ColumnType.Wildcard:
                    break;
                // Only the last part counts, so c.EquipmentName and dbo.Certificates.EquipmentName both resolve to EquipmentName.
                case ColumnType.Regular
                    when node.MultiPartIdentifier?.Identifiers.LastOrDefault() is { } column
                        && AllowedColumns.Contains(column.Value):
                    break;
                // Anything else: a disallowed name, or a pseudo-column such as $IDENTITY or $ROWGUID.
                default:
                    Reason ??= SqlGuardReasons.ColumnNotAllowed;
                    break;
            }
        }

        public override void Visit(SelectStarExpression node)
        {
            Reason ??= SqlGuardReasons.SelectStarNotAllowed;
        }
    }
}
