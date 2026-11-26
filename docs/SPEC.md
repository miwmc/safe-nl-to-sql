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
A rejected query returns 200 with `rejected: true`, a `reason` and empty `rows`. Invalid input (a body that cannot be read, an empty question, over 500 characters) returns 400. If the SQL generator fails, the response is 503 with a generic message: no exception text, model output or SQL.

## SqlGuard rules
Parse with ScriptDom (TSql160Parser). Regex is not enough. Reject when:
1. The SQL does not parse (a parser exception counts as not parseable), or it is longer than 2000 characters. The length is checked before parsing, which bounds nesting depth.
2. There is not exactly one statement (zero statements, such as empty or comment-only input, is rejected as well as more than one).
3. The statement is not a SELECT (INSERT, UPDATE, DELETE, MERGE, DROP, EXEC, etc.).
4. It references any table other than dbo.Certificates (including joins, subqueries, CTEs over other tables, system views like sys.tables or INFORMATION_SCHEMA). The schema and brackets are optional, names are case-insensitive, and database- or server-qualified names are rejected. A CTE name is only valid after its definition (no forward or self reference); a CTE whose body reads its own name unqualified is rejected, including a CTE named Certificates reading `Certificates`, which SQL Server treats as a recursive reference (`dbo.Certificates` inside that body is the real table and is fine). dbo.Certificates may be referenced at most 2 times in the whole query, at every depth (joins, subqueries, derived tables, UNION branches, CTE bodies), otherwise `TooManyTableReferences` ("At most 2 references to dbo.Certificates are allowed"). This bounds self-joins and cross joins. A CTE counts once per use, because SQL Server expands its body at every use site (a CTE over one reference, used twice, counts 2), and an unused CTE counts zero. The outer query, and each UNION branch, must have a FROM clause, so `SELECT TOP 1 GETDATE()` is rejected.
5. It selects or filters on a column outside the allowlist: CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant. ClientName and `SELECT *` are rejected. The allowlist is case-insensitive and applies in every clause and at every depth (WHERE, ORDER BY, subqueries, CTEs, UNION branches). `COUNT(*)` is allowed because it returns no column data. A `*` is allowed only as the sole, unqualified argument of COUNT; anywhere else (`MIN(*)`, `COUNT_BIG(*)`, `COUNT(MAX(*))`, `c.*`) it is rejected as `SelectStarNotAllowed`. References to column aliases (for example `ORDER BY` an output alias, or a CTE or derived-table rename) are rejected; `AS` aliases on the output are fine.
6. It uses a date filter on any column other than CalibrationDate. A date-valued expression is a date function (GETDATE, SYSDATETIME, DATEADD, DATEDIFF, YEAR, MONTH, DAY), CURRENT_TIMESTAMP, or a CAST/CONVERT/TRY_CAST to a date type. Columns inside one must be CalibrationDate, and a comparison, BETWEEN, IN or LIKE that contains one may only involve CalibrationDate. CalibrationDate is the only date column, so this rule mostly guards against nonsense comparisons. Comparing CalibrationDate with a text column, or a text column with a date-like string, is not a date filter and is not checked here. Known limitation: the check looks at every column inside a comparison, so some valid date filters that mix a CASE on another column into the date expression, such as `CalibrationDate > GETDATE() - CASE WHEN IsCompliant = 1 THEN 30 ELSE 60 END`, are rejected. Rejecting a valid query is the safe direction; the user can rephrase.
7. It uses SELECT INTO, OPENROWSET, OPENQUERY, any FOR clause (FOR XML, FOR JSON, FOR BROWSE), any OPTION query hint or table hint (`WITH (NOLOCK)`, `WITH (INDEX(...))`, and so on), or any function outside the allowlist: COUNT, MIN, MAX, GETDATE, SYSDATETIME, DATEADD, DATEDIFF, YEAR, MONTH, DAY, LOWER, UPPER, CAST, CONVERT, TRY_CAST, TRY_CONVERT and CURRENT_TIMESTAMP (case-insensitive). Schema-qualified calls such as `dbo.GETDATE()`, `@@` global variables, functions with an OVER clause, and function-like syntax (IIF, LEFT, RIGHT, PARSE, NEXT VALUE FOR, ODBC `{fn}`) are rejected too. CASE, COALESCE and NULLIF are expressions, not function calls, and stay allowed. Checked at every depth (subqueries, CTEs, derived tables, UNION branches). OPENROWSET and OPENQUERY are table sources, so rule 4 rejects them first. Expressions and predicates fail closed: every expression and predicate node must be of a type on an explicit allowlist (column references, string, integer, numeric, NULL and MAX literals, arithmetic and parentheses, scalar subqueries, the functions above, CASE/COALESCE/NULLIF, comparisons, AND/OR/NOT, IS NULL, BETWEEN, IN, LIKE and EXISTS), and anything else is rejected, for example CONTAINS and FREETEXT, `@` variables, AT TIME ZONE, ANY/SOME/ALL comparisons and IS DISTINCT FROM. The allowlist covers expressions and predicates only: table sources are covered by rule 4, and TOP and OFFSET by rule 8.
8. The outer query, and each branch of a UNION/EXCEPT/INTERSECT, has no TOP, or its TOP is not an integer literal from 0 to 100 (plain or in parentheses, at any depth). A missing TOP, TOP PERCENT, WITH TIES, a non-literal value such as `TOP (5 + 5)` or `TOP (SELECT ...)`, or OFFSET/FETCH instead of TOP is rejected as `TopMissing`; a value above 100 is `TopTooLarge`. The TOP values of all outer branches are added up and the sum must be at most 100, otherwise `TopTooLarge` (so `TOP 60 ... UNION SELECT TOP 60 ...` is rejected, and the 2-reference limit of rule 4 means at most two branches in practice). `TOP (@x)` is rejected earlier, by the rule 7 expression allowlist. TOP is never added automatically (decided in PR 3): a rejected query returns an empty result with a reason, and the guard never repairs a query. Subqueries, derived tables and CTE bodies need no TOP.

Return a result type such as `GuardResult(bool IsAllowed, string? Reason)`.

## LLM generation
- `ISqlGenerator.GenerateAsync(string question, CancellationToken ct)` returns SQL text.
- The Azure OpenAI implementation sends the schema (allowed columns only) and the rules in a system prompt, and asks for a single T-SQL SELECT or the literal `NO_QUERY` when the question cannot be answered.
- It uses the Responses API on the Azure v1 endpoint with `store: false`. A response whose status is not `completed` is never SQL; it is a generator failure (503), unless the content filter caused it (see Defence in depth): a truncated query can still be valid and broader than the question. The model output is never stripped or repaired.
- The Azure generator is used only when the `AzureOpenAI` section exists, and startup fails when that section is incomplete.
- The question is never logged. The generated SQL is logged at Information and can contain literal values taken from the question. A failed request is reported with its HTTP status and Azure's error code only, never the server's error message.
- `NO_QUERY` maps to an empty, rejected result with a reason.
- A `FakeSqlGenerator` returns canned SQL per question for tests.

## Defence in depth
1. Prompt: tells the model the rules (can be ignored by the model).
2. SqlGuard: parses and enforces the rules in code.
3. Database: the API connects as `nl_search_reader`, which has SELECT on the six allowed columns of dbo.Certificates (not ClientName) and nothing else, so even if 1 and 2 fail, the database refuses ClientName and other tables.
4. Content filter, outside our code: Azure OpenAI can block a question or the model's answer. Nothing relies on it. A block (HTTP 400 with code `content_filter`, or a response left incomplete by the filter) returns 200 rejected with the reason `ContentFiltered`, because a retry cannot help.

Known limitation: the guard can approve a query that the database denies (error 230). The reader only has column-level SELECT, and when a table reference uses no allowed column anywhere in its scope, SQL Server asks for SELECT on every column of the table. Examples: a bare `SELECT COUNT(*) FROM dbo.Certificates`, `COUNT(1)`, `SELECT TOP 1 1 ...`, or `EXISTS (SELECT 1 FROM dbo.Certificates)` with no inner filter. This is the fail-closed direction, and the guard deliberately does not copy SQL Server's permission rules: both layers fail closed and need not agree. `COUNT(*)` with a filter on an allowed column works.

Known limitation: API key auth; Entra ID with managed identity would be the production choice.

Known limitation: test hosts ignore unprefixed and `DOTNET_` `AzureOpenAI` variables and user-secrets, but `ASPNETCORE_AzureOpenAI__` variables still reach them through WebApplicationFactory's command-line arguments; do not set them.

Known limitation: if the generator's time budget runs out while the SDK waits to retry a failed request, such as a 429, only a timeout is logged, without that request's HTTP status or Azure error code.

## Query errors
The guard can also approve a query that SQL Server cannot run. The executor returns `QueryResult.Failed(QueryExecutionReasons.QueryFailed)`, an empty rejected result with a fixed reason, only when both are true:
- the error is raised while running the approved SQL or reading its rows, not while opening the connection or setting up the session;
- its number is on this list: 241, 242, 245 (conversion failures), 8114 (data type conversion), 8115 (arithmetic overflow), 8120 (missing GROUP BY), 8134 (divide by zero).

The full exception is logged. The database message is never returned, and the SQL is never retried or repaired. Every other error is thrown, so `/search` returns 500 with no exception text: this includes connection and login failures, invalid column or object names (207, 208, which point to a wrong schema), lock timeouts and deadlocks. Severity cannot tell these apart, because several of them share severity 16 with the listed errors.

The list is deliberately small. It grows only when 5b shows the model producing a new error number.

## Pull request plan
1. Scaffold: solution, projects, xUnit, CLAUDE.md, docs, GitHub Actions running build and test.
2. SqlGuard rules 1-3, tests first.
3. SqlGuard rules 4-8, tests first.
4. Database: docker compose with SQL Server, schema, seed data, read-only user, query execution with Dapper. The executor must run the approved SQL with `QUOTED_IDENTIFIER ON` (the setting SqlGuard's parser assumes, so `"ClientName"` is an identifier for both) and with a command timeout, because the guard bounds rows and table references but not execution time. The executor maps database errors 229 and 230 to `QueryResult.Failed(QueryExecutionReasons.QueryNotPermitted)`, an empty result with a reason.
5. (Done) ISqlGenerator with Azure OpenAI, the /search endpoint, integration tests with the fake generator. The endpoint turns a failed `QueryResult` (`QueryNotPermitted`, `QueryTimeout`, `QueryFailed`) into a rejected response with that reason. The system prompt tells the model to always reference an allowed column in every table reference (for an unfiltered count, use `COUNT(CertificateNumber)`).
6. README: what it does, how to run, and "How I built this with Claude Code".

## Out of scope
Frontend, authentication, deployment, caching, conversation history.
