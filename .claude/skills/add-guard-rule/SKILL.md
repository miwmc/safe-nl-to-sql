---
name: add-guard-rule
description: Add or change a SqlGuard validation rule test-first. Use whenever a task adds, changes or fixes a rule in SqlGuard.
---
# Add a SqlGuard rule (tests first)

Rule to add: $ARGUMENTS

1. Read the SqlGuard rules in docs/SPEC.md and the existing tests in tests/SafeNlToSql.UnitTests/SqlGuardTests.cs.
2. Write the tests first, as an xUnit `[Theory]` with `[InlineData]`:
   - at least one query the rule must accept;
   - at least one query it must reject, with the expected reason;
   - one tricky case (comments, casing, brackets, aliases, subqueries, UNION) that a naive check would miss.
3. Run `dotnet test --filter "FullyQualifiedName~SqlGuardTests"` and show me the new tests failing. Stop and wait for my review of the tests.
4. After I approve, implement the rule in SqlGuard using the ScriptDom AST (a TSqlFragmentVisitor), not string matching.
5. Run the same filtered tests until they pass, then the full suite once.
6. If the rule changes behaviour described in docs/SPEC.md, update the spec in the same change.
7. Summarise: the rule, the test cases added, and anything you were unsure about.
