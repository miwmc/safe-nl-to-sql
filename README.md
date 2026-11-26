# safe-nl-to-sql

Ask a question about equipment calibration certificates in plain language, and get rows back. An LLM on Azure OpenAI turns the question into T-SQL, but the model is never trusted. Two safety layers that do not depend on it are built and tested. SqlGuard parses and checks every generated query before it can run, and approved queries run as a read-only database user that has SELECT on six columns only. A rejected query returns a reason instead of being repaired. The `/search` endpoint connects the model to these layers. It is tested with a fake generator, and opt-in live tests run it against a real deployment.

## Status

In progress. Pull request plan:

- [x] 1. Scaffold: solution, projects, xUnit, docs, GitHub Actions build and test
- [x] 2. SqlGuard rules 1-3, tests first
- [x] 3. SqlGuard rules 4-8, tests first
- [x] 4. Database: docker compose with SQL Server, schema, seed data, read-only user, query execution with Dapper
- [x] 5. ISqlGenerator with Azure OpenAI, the `/search` endpoint, integration tests with the fake generator
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

The unit tests need no database and no LLM. They cover every SqlGuard rule with accept, reject and tricky cases. The integration tests start SQL Server in a container, so **Docker must be running** for `dotnet test`.

## Run locally

1. Copy `.env.example` to `.env` and set `MSSQL_SA_PASSWORD` and `READER_PASSWORD`. Both must meet SQL Server's complexity policy and must not contain `'`, `"` or `$`. `.env` is git-ignored.
2. Start the database: `docker compose up -d`. SQL Server starts, then a one-shot `db-init` service runs `db/*.sql` (schema, invented seed data, and the read-only `nl_search_reader` login) and exits.
3. Check that the init finished: `docker compose ps -a` must show `db-init` as `Exited (0)`. `up -d` returns before `db-init` is done, so wait a few seconds and run it again if it is still running. If it shows any other exit code, read the reason with `docker compose logs db-init` (a `READER_PASSWORD` that fails the complexity policy is a common cause) and fix it before you go on.
4. Store the reader connection string as a user secret, using the `READER_PASSWORD` from `.env`:

```bash
dotnet user-secrets set "ConnectionStrings:Reader" "Server=localhost,1433;Database=SafeNlToSql;User Id=nl_search_reader;Password=<READER_PASSWORD>;TrustServerCertificate=True" --project src/SafeNlToSql.Api
```

The API reads the reader connection string at startup and does not start without it.

5. Store the Azure OpenAI settings as user secrets. The generator is tested with `gpt-4.1-mini`. Reasoning models are not supported (it sends temperature 0 and a 300 token output limit by default). First the endpoint and the deployment, replacing each placeholder with your own value:

```bash
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/openai/v1/" --project src/SafeNlToSql.Api
dotnet user-secrets set "AzureOpenAI:Deployment" "<deployment-name>" --project src/SafeNlToSql.Api
```

Then the API key. It is read without echo, so it never lands in shell history. Run these bash lines one at a time: if they are pasted together, `read` can take the next line as the key.

```bash
read -rsp "API key: " AOAI_KEY; echo
dotnet user-secrets set "AzureOpenAI:ApiKey" "$AOAI_KEY" --project src/SafeNlToSql.Api
unset AOAI_KEY
```

Without these three settings the API still starts, but a valid question to `/search` returns 503. If you set only some of them, the API does not start.

6. Run the API with `dotnet run --project src/SafeNlToSql.Api`, then ask a question from another terminal:

```bash
curl -X POST http://localhost:5140/search \
  -H "Content-Type: application/json" \
  -d '{"question": "show me all non-compliant microwaves calibrated in the last 30 days"}'
```

7. Stop the database with `docker compose down`.

### Live tests

The live tests send real questions to your Azure OpenAI deployment. They need the user secrets from step 5, and Docker running (they start their own SQL Server). They run only when `LIVE_AZURE_OPENAI` is exactly `1`; otherwise they are skipped, also in a plain `dotnet test`.

There are 13 live cases. Each run sends one request per case, plus one more when the SDK retries a transient failure. Requests are billed as normal Azure OpenAI usage, although a request blocked by the content filter may not be billed.

```bash
LIVE_AZURE_OPENAI=1 dotnet test tests/SafeNlToSql.IntegrationTests --filter "Category=LiveAzureOpenAI"
```

Set the variable on the command as shown instead of exporting it, so later test runs from the same shell do not call Azure. Add `--logger "console;verbosity=detailed"` to see each test's generated SQL and, for a rejection, which layer stopped it.

## Tech stack

- .NET 10 Minimal API
- Microsoft.SqlServer.TransactSql.ScriptDom for SQL validation
- SQL Server in Docker, accessed with Dapper
- Azure OpenAI for SQL generation
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
