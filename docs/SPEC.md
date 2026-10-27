# Spec: safe-nl-to-sql

## Problem
Users want to search a table of equipment certificates in plain language, for example "show me all non-compliant microwaves calibrated in the last 30 days". An LLM can turn that into SQL, but an LLM can also produce SQL that reads the wrong data, changes data, or is simply wrong. Prompt instructions alone are not enough, because they rely on the model following them.

This project adds two layers that do not depend on the model: a validator that parses the SQL before it runs, and a database user that can only read one table.

## Data model
Table `dbo.Certificates` (invented seed data, about 50 rows):

| Column | Type | Notes |
|---|---|---|
| Id | int, PK | |
| CertificateNumber | nvarchar(50) | |
| EquipmentName | nvarchar(100) | e.g. "Fridge FR-200" |
| EquipmentType | nvarchar(50) | Fridge, Microwave, Microphone, Speaker |
| Metric | nvarchar(50) | Temperature, Frequency, AcousticLevel, Linearity |
| CalibrationDate | date | |
| IsCompliant | bit | |
| ClientName | nvarchar(100) | sensitive: must never be returned |

## API
`POST /search`

Request: `{ "question": "string" }`

Response 200:
```json
{
  "sql": "SELECT ... (the executed SQL, or null if rejected)",
  "rejected": false,
  "reason": null,
  "rows": [ { "certificateNumber": "...", "equipmentName": "...", "...": "..." } ]
}
```
A rejected query returns 200 with `rejected: true`, a `reason` and empty `rows`. Invalid input (empty question, over 500 characters) returns 400.

## SqlGuard rules
Parse with ScriptDom (TSql160Parser). Regex is not enough. Reject when:
1. The SQL does not parse (a parser exception counts as not parseable), or it is longer than 2000 characters. The length is checked before parsing, which bounds nesting depth.
2. There is not exactly one statement (zero statements, such as empty or comment-only input, is rejected as well as more than one).
3. The statement is not a SELECT (INSERT, UPDATE, DELETE, MERGE, DROP, EXEC, etc.).
4. It references any table other than dbo.Certificates (including joins, subqueries, CTEs over other tables, system views like sys.tables or INFORMATION_SCHEMA). The schema and brackets are optional, names are case-insensitive, and database- or server-qualified names are rejected. A CTE name is only valid after its definition (no forward or self reference). The outer query, and each UNION branch, must have a FROM clause, so `SELECT TOP 1 GETDATE()` is rejected.
5. It selects or filters on a column outside the allowlist: CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant. ClientName and `SELECT *` are rejected. The allowlist is case-insensitive and applies in every clause and at every depth (WHERE, ORDER BY, subqueries, CTEs, UNION branches). `COUNT(*)` is allowed because it returns no column data. References to column aliases (for example `ORDER BY` an output alias, or a CTE or derived-table rename) are rejected; `AS` aliases on the output are fine.
6. It uses a date filter on any column other than CalibrationDate. A date-valued expression is a date function (GETDATE, SYSDATETIME, DATEADD, DATEDIFF, YEAR, MONTH, DAY), CURRENT_TIMESTAMP, or a CAST/CONVERT/TRY_CAST to a date type. Columns inside one must be CalibrationDate, and a comparison, BETWEEN, IN or LIKE that contains one may only involve CalibrationDate. CalibrationDate is the only date column, so this rule mostly guards against nonsense comparisons. Comparing CalibrationDate with a text column, or a text column with a date-like string, is not a date filter and is not checked here.
7. It uses SELECT INTO, OPENROWSET, OPENQUERY, any FOR clause (FOR XML, FOR JSON, FOR BROWSE), any OPTION query hint or table hint (`WITH (NOLOCK)`, `WITH (INDEX(...))`, and so on), or any function outside the allowlist: COUNT, MIN, MAX, GETDATE, SYSDATETIME, DATEADD, DATEDIFF, YEAR, MONTH, DAY, LOWER, UPPER, CAST, CONVERT, TRY_CAST, TRY_CONVERT and CURRENT_TIMESTAMP (case-insensitive). Schema-qualified calls such as `dbo.GETDATE()`, `@@` global variables, functions with an OVER clause, and function-like syntax (IIF, LEFT, RIGHT, PARSE, NEXT VALUE FOR, ODBC `{fn}`) are rejected too. CASE, COALESCE and NULLIF are expressions, not function calls, and stay allowed. Checked at every depth (subqueries, CTEs, derived tables, UNION branches). OPENROWSET and OPENQUERY are table sources, so rule 4 rejects them first. Expressions and predicates fail closed: every expression and predicate node must be of a type on an explicit allowlist (column references, string, integer, numeric, NULL and MAX literals, arithmetic and parentheses, scalar subqueries, the functions above, CASE/COALESCE/NULLIF, comparisons, AND/OR/NOT, IS NULL, BETWEEN, IN, LIKE and EXISTS), and anything else is rejected, for example CONTAINS and FREETEXT, `@` variables, AT TIME ZONE, ANY/SOME/ALL comparisons and IS DISTINCT FROM. The allowlist covers expressions and predicates only: table sources are covered by rule 4, and TOP and OFFSET by rule 8.
8. The outer query, and each branch of a UNION/EXCEPT/INTERSECT, has no TOP, or its TOP is not an integer literal from 0 to 100 (plain or in parentheses, at any depth). A missing TOP, TOP PERCENT, WITH TIES, a non-literal value such as `TOP (5 + 5)` or `TOP (SELECT ...)`, or OFFSET/FETCH instead of TOP is rejected as `TopMissing`; a value above 100 is `TopTooLarge`. `TOP (@x)` is rejected earlier, by the rule 7 expression allowlist. TOP is never added automatically (decided in PR 3): a rejected query returns an empty result with a reason, and the guard never repairs a query. Subqueries, derived tables and CTE bodies need no TOP.

Return a result type such as `GuardResult(bool IsAllowed, string? Reason)`.

## LLM generation
- `ISqlGenerator.GenerateAsync(string question, CancellationToken ct)` returns SQL text.
- The Azure OpenAI implementation sends the schema (allowed columns only) and the rules in a system prompt, and asks for a single T-SQL SELECT or the literal `NO_QUERY` when the question cannot be answered.
- `NO_QUERY` maps to an empty, rejected result with a reason.
- A `FakeSqlGenerator` returns canned SQL per question for tests.

## Defence in depth
1. Prompt: tells the model the rules (can be ignored by the model).
2. SqlGuard: parses and enforces the rules in code.
3. Database: the API connects as `nl_search_reader`, which has SELECT on dbo.Certificates and nothing else. Even if 1 and 2 fail, the database refuses.

## Pull request plan
1. Scaffold: solution, projects, xUnit, CLAUDE.md, docs, GitHub Actions running build and test.
2. SqlGuard rules 1-3, tests first.
3. SqlGuard rules 4-8, tests first.
4. Database: docker compose with SQL Server, schema, seed data, read-only user, query execution with Dapper.
5. ISqlGenerator with Azure OpenAI, the /search endpoint, integration tests with the fake generator.
6. README: what it does, how to run, and "How I built this with Claude Code".

## Out of scope
Frontend, authentication, deployment, caching, conversation history.
