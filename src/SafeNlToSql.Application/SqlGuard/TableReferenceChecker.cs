using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SafeNlToSql.Application;

/// <summary>
/// Rule 4: the outer query reads from dbo.Certificates, and nothing else is referenced anywhere
/// (joins, subqueries, derived tables, UNION branches, CTE bodies).
/// </summary>
internal static class TableReferenceChecker
{
    private const string AllowedSchema = "dbo";
    private const string AllowedTable = "Certificates";

    public static bool IsAllowed(SelectStatement select)
    {
        if (!ReadsFromTable(select.QueryExpression))
        {
            return false;
        }

        var visitor = new TableVisitor();

        // A CTE only sees the CTEs defined before it. Declaring each name after its body is
        // visited keeps a forward reference (resolved by SQL Server to a real table) from
        // being mistaken for a CTE.
        foreach (var cte in select.WithCtesAndXmlNamespaces?.CommonTableExpressions ?? [])
        {
            cte.QueryExpression.Accept(visitor);
            visitor.DeclareCte(cte.ExpressionName.Value);
        }

        select.QueryExpression.Accept(visitor);
        return !visitor.HasViolation;
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
        private readonly HashSet<string> _cteNames = new(StringComparer.OrdinalIgnoreCase);

        public bool HasViolation { get; private set; }

        public void DeclareCte(string name) => _cteNames.Add(name);

        // Called for every fragment, so a table source kind we do not know about fails closed.
        public override void Visit(TSqlFragment node)
        {
            if (node is not TableReference reference)
            {
                return;
            }

            switch (reference)
            {
                case NamedTableReference named when IsAllowedName(named.SchemaObject):
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

        private bool IsAllowedName(SchemaObjectName name)
        {
            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            {
                return false;
            }

            if (name.SchemaIdentifier is null && _cteNames.Contains(name.BaseIdentifier.Value))
            {
                return true;
            }

            return string.Equals(name.BaseIdentifier.Value, AllowedTable, StringComparison.OrdinalIgnoreCase)
                && (name.SchemaIdentifier is null
                    || string.Equals(name.SchemaIdentifier.Value, AllowedSchema, StringComparison.OrdinalIgnoreCase));
        }
    }
}
