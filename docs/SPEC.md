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
1. The SQL does not parse.
2. There is not exactly one statement (zero statements, such as empty or comment-only input, is rejected as well as more than one).
3. The statement is not a SELECT (INSERT, UPDATE, DELETE, MERGE, DROP, EXEC, etc.).
4. It references any table other than dbo.Certificates (including joins, subqueries, CTEs over other tables, system views like sys.tables or INFORMATION_SCHEMA).
5. It selects or filters on a column outside the allowlist: CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant. ClientName and `SELECT *` are rejected.
6. It uses a date filter on any column other than CalibrationDate.
7. It uses SELECT INTO, OPENROWSET, OPENQUERY, or any function outside a small allowlist (e.g. GETDATE, DATEADD, COUNT).
8. It has no TOP or TOP is above 100 (or add TOP 100 automatically; decide during PR 2 and document the choice).

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
2. SqlGuard, tests first, covering every rule above.
3. Database: docker compose with SQL Server, schema, seed data, read-only user, query execution with Dapper.
4. ISqlGenerator with Azure OpenAI, the /search endpoint, integration tests with the fake generator.
5. README: what it does, how to run, and "How I built this with Claude Code".

## Out of scope
Frontend, authentication, deployment, caching, conversation history.
