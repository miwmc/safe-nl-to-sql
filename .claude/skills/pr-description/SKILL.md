---
name: pr-description
description: Draft the pull request description for the current branch from the diff and test results.
disable-model-invocation: true
---
# Draft the PR description

1. Run `git diff main...HEAD --stat` and `git log main..HEAD --oneline` to see what changed.
2. Run `dotnet test` and note the result.
3. Write the draft to `.pr-description.md` (gitignored) in this format:

```
## What
<one or two lines on what this PR does and which PR from docs/SPEC.md it is>

## How I used Claude Code
- Plan I approved / changed: TODO (Lino)
- What it got right: TODO (Lino)
- What I changed by hand and why: TODO (Lino)

## Tests
<what is covered, the command to run them, and the result>
```

4. Fill in only "What" and "Tests". Leave every "How I used Claude Code" line as `TODO (Lino)`: that section is my own account of the work and must not be written or guessed by you.
5. Tell me the file is ready. I open the PR myself with `gh pr create --body-file .pr-description.md`.
