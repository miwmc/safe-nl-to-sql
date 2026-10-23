# safe-nl-to-sql

.NET 10 Minimal API: natural language question -> LLM-generated T-SQL -> SqlGuard validation -> execution as a read-only database user. Scope, data model, SqlGuard rules and PR plan: @docs/SPEC.md

# Commands
- Build: `dotnet build`
- One test class: `dotnet test --filter "FullyQualifiedName~<ClassName>"` (prefer this over the full suite while iterating)
- Full suite: `dotnet test` (run once before saying a task is done)
- Database: `docker compose up -d` / `docker compose down`
- Run API: `dotnet run --project src/SafeNlToSql.Api`
- A hook filters plain `dotnet test` output to failures and the summary. For full output, run `dotnet test ... 2>&1 | tail -200`.

# Architecture
- src/SafeNlToSql.Api (endpoints, DI), src/SafeNlToSql.Application (SqlGuard, ISqlGenerator, use case), src/SafeNlToSql.Infrastructure (Azure OpenAI, Dapper, seed). Api -> Application <- Infrastructure; Application never references Infrastructure.
- SqlGuard parses SQL with Microsoft.SqlServer.TransactSql.ScriptDom (TSql160Parser). No regex validation.
- Unit tests never call the LLM: use FakeSqlGenerator.
- Target net10.0, SDK pinned in global.json. Nullable enabled, warnings as errors in src.
- Async/await for all I/O; never .Result or .Wait().
- Secrets only via IConfiguration (dotnet user-secrets locally, env vars in CI).

# Rules
- IMPORTANT: never execute generated SQL unless SqlGuard approved it.
- The nl_search_reader login gets SELECT on dbo.Certificates and nothing else.
- On rejection return an empty result with a reason. Never repair or guess the query.
- Seed data is invented. No real client data.

# Workflow
- Verify your own work: after a change, build, run the relevant tests, and iterate until they pass. Show me the command and its result, not just "done".
- Work in small steps and stop after each one so I can review the diff.
- Do not commit or push. I review and commit myself.
- If something is ambiguous, ask instead of guessing.
- New SqlGuard rule: use the add-guard-rule skill.

# When compacting
- Keep the list of changed files, the current PR goal from docs/SPEC.md, and the test commands with their last result.
