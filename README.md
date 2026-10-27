# safe-nl-to-sql

The goal: ask a question about equipment calibration certificates in plain language, and get rows back. An LLM turns the question into T-SQL, but the model is never trusted. Today the core safety layer, SqlGuard, is built and tested: every generated query is parsed and checked before it can run. Next, approved queries will run as a database user that can only read one table, and a rejected query returns a reason instead of being repaired.

## Status

In progress. Pull request plan:

- [x] 1. Scaffold: solution, projects, xUnit, docs, GitHub Actions build and test
- [x] 2. SqlGuard rules 1-3, tests first
- [x] 3. SqlGuard rules 4-8, tests first
- [ ] 4. Database: docker compose with SQL Server, schema, seed data, read-only user, query execution with Dapper
- [ ] 5. ISqlGenerator with Azure OpenAI, the `/search` endpoint, integration tests with the fake generator
- [ ] 6. README: what it does, how to run, and how it was built

## SqlGuard rules

SqlGuard parses the SQL with Microsoft's ScriptDom (T-SQL 2022 grammar). It does not use regex. A query is rejected when:

| # | Rule | In plain English |
|---|---|---|
| 1 | Parseable and short | The SQL must parse and be at most 2000 characters. |
| 2 | One statement | Exactly one statement. Empty input and multiple statements are rejected. |
| 3 | SELECT only | No INSERT, UPDATE, DELETE, MERGE, DROP, EXEC and so on. |
| 4 | One table | Only `dbo.Certificates`, referenced at most twice. No other tables, system views, or linked servers. |
| 5 | Allowed columns | Only CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate and IsCompliant. No `ClientName`, no `SELECT *`. |
| 6 | Date filters | Date filters may only involve CalibrationDate. |
| 7 | Allowed features | Only an allowlist of functions and expressions. No SELECT INTO, OPENROWSET, OPENQUERY, FOR clauses, or query and table hints. |
| 8 | Bounded rows | The outer query needs a literal `TOP` of 100 or less (summed across UNION branches). |

SqlGuard fails closed: anything it does not recognise is rejected.

The full rules are in [docs/SPEC.md](docs/SPEC.md).

## Build and test

Requires the .NET SDK pinned in `global.json` (10.0).

```bash
dotnet build
dotnet test
```

The tests need no database and no LLM. They cover every SqlGuard rule with accept, reject and tricky cases.

## Tech stack

- .NET 10 Minimal API
- Microsoft.SqlServer.TransactSql.ScriptDom for SQL validation
- SQL Server in Docker, accessed with Dapper (PR 4)
- Azure OpenAI for SQL generation (PR 5)
- xUnit, and GitHub Actions for CI

## How I built this with Claude Code

I wrote this project with Claude Code in the terminal, as a test of AI-assisted development with a human in charge of every decision. Claude writes most of the code while I set the rules, review every plan and test run, and make every commit.

### Setup

- **Lean context.** A short `CLAUDE.md` (commands, architecture, rules) and `docs/SPEC.md` as the single source of truth. I clear the session between tasks.
- **Guardrails in `.claude/settings.json`.** Claude cannot commit, push, run `git reset --hard`, or read secret files. I commit every change myself.
- **A hook for test output.** A PreToolUse hook rewrites a plain `dotnet test` so only failures and the summary come back, which keeps the context small.
- **Skills.** `add-guard-rule` encodes the test-first workflow for a new SqlGuard rule, and `pr-description` drafts each PR body.

### Workflow

1. Plan mode first. I read the plan and change it before any code is written.
2. Tests first. Claude writes accept, reject and tricky cases, runs them red, and stops for my review.
3. I check that the red run is red for the right reason: every reject case must fail on "was allowed", not on a different rejection reason, which proves the SQL actually parses.
4. Claude implements, runs the rule's tests and then the full suite. I commit.
5. Before each PR, a `/code-review` in a fresh session. I decide which findings to fix, document or defer.

### Decisions I made

- SqlGuard fails closed. Rule 7 started as a list of forbidden constructs; I changed it to an allowlist of expression types, so anything unknown is rejected.
- The guard never repairs a query. A missing `TOP` is rejected, not added.
- Kept rule 6 simple (date-valued expressions only) and recorded its known limitation in the spec.
- After the code review: at most 2 references to the table and `TOP` summed across UNION branches, to bound the cost of a query.

### Where Claude helped most

- It found a CTE scoping bypass on its own (a later CTE named like a real table) and closed it with tests.
- When two tests failed, it checked how the parser actually represents large numbers instead of changing the tests.
- It built the expression allowlist from the node types in the accepted test queries, using a throwaway program outside the repo.
- It stopped and asked when something was unclear, for example missing project names or a broken local .NET install, instead of guessing.

### Where I corrected it

- It tried to edit a C# file through a Python heredoc and to pipe test output past my hook. I rejected both and made the plain commands a rule in `CLAUDE.md`.
- I rejected a review finding whose premise was wrong.
- I spotted that a failing-test count alone doesn't prove each reject case parses, and added the failure-type check to the workflow.

My running notes for each PR are in the PR descriptions.
