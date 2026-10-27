using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 4: the outer query reads from dbo.Certificates, and nothing else is referenced anywhere
/// (joins, subqueries, derived tables, UNION branches, CTE bodies). The table may be referenced at most
/// twice, which bounds self-joins and cross joins. SQL Server expands a CTE body at every use site, so a
/// CTE counts once per use, and an unused CTE counts zero.
/// </summary>
internal static class TableReferenceChecker
{
    private const string AllowedSchema = "dbo";
    private const string AllowedTable = "Certificates";
    private const int MaxReferences = 2;

    /// <returns>The rejection reason, or null when the table references are allowed.</returns>
    public static string? FindViolation(SelectStatement select)
    {
        if (!ReadsFromTable(select.QueryExpression))
        {
            return SqlGuardReasons.TableNotAllowed;
        }

        var visitor = new TableVisitor();

        // A CTE only sees the CTEs defined before it. Declaring each name after its body is
        // visited keeps a forward reference (resolved by SQL Server to a real table) from
        // being mistaken for a CTE.
        foreach (var cte in select.WithCtesAndXmlNamespaces?.CommonTableExpressions ?? [])
        {
            visitor.VisitCteBody(cte);
        }

        select.QueryExpression.Accept(visitor);
        if (visitor.HasViolation)
        {
            return SqlGuardReasons.TableNotAllowed;
        }

        return visitor.References > MaxReferences ? SqlGuardReasons.TooManyTableReferences : null;
    }

    // Every branch of the outer query needs a FROM; the table references inside it are checked by the visitor.
    private static bool ReadsFromTable(QueryExpression query) => query switch
    {
        QuerySpecification spec => spec.FromClause is { TableReferences.Count: > 0 },
        BinaryQueryExpression binary => ReadsFromTable(binary.FirstQueryExpression)
            && ReadsFromTable(binary.SecondQueryExpression),
        QueryParenthesisExpression parenthesis => ReadsFromTable(parenthesis.QueryExpression),
        _ => false,
    };

    private sealed class TableVisitor : TSqlFragmentVisitor
    {
        // Each declared CTE and the number of references to the table one use of it expands to.
        private readonly Dictionary<string, int> _cteCosts = new(StringComparer.OrdinalIgnoreCase);

        // The CTE whose body is being visited. An unqualified reference to its own name is a recursive
        // self-reference in SQL Server, even when the name is also the allowed table's.
        private string? _currentCte;

        public bool HasViolation { get; private set; }

        // References counted so far, saturated just above the limit so nested CTEs cannot overflow it.
        public int References { get; private set; }

        public void VisitCteBody(CommonTableExpression cte)
        {
            var outer = References;
            References = 0;
            _currentCte = cte.ExpressionName.Value;
            cte.QueryExpression.Accept(this);
            _currentCte = null;

            // Declared after its body is visited, so a forward reference is not mistaken for the CTE.
            _cteCosts[cte.ExpressionName.Value] = References;
            References = outer;
        }

        private void AddReferences(int count) => References = Math.Min(References + count, MaxReferences + 1);

        // Called for every fragment, so a table source kind we do not know about fails closed.
        public override void Visit(TSqlFragment node)
        {
            if (node is not TableReference reference)
            {
                return;
            }

            switch (reference)
            {
                case NamedTableReference named when TryCount(named.SchemaObject):
                // Containers: the tables inside them are visited as children.
                case QueryDerivedTable:
                case JoinTableReference:
                case JoinParenthesisTableReference:
                    break;
                default:
                    HasViolation = true;
                    break;
            }
        }

        // Counts the references the name stands for. False when the name is not allowed at all.
        private bool TryCount(SchemaObjectName name)
        {
            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            {
                return false;
            }

            if (name.SchemaIdentifier is null
                && string.Equals(name.BaseIdentifier.Value, _currentCte, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (name.SchemaIdentifier is null && _cteCosts.TryGetValue(name.BaseIdentifier.Value, out var cost))
            {
                AddReferences(cost);
                return true;
            }

            if (string.Equals(name.BaseIdentifier.Value, AllowedTable, StringComparison.OrdinalIgnoreCase)
                && (name.SchemaIdentifier is null
                    || string.Equals(name.SchemaIdentifier.Value, AllowedSchema, StringComparison.OrdinalIgnoreCase)))
            {
                AddReferences(1);
                return true;
            }

            return false;
        }
    }
}
