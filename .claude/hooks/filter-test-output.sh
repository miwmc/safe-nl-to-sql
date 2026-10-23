#!/bin/bash
# PreToolUse hook: when Claude runs a plain `dotnet test`, rewrite the command
# so only failures, build errors and the summary reach the context.
input=$(cat)
cmd=$(echo "$input" | jq -r '.tool_input.command // empty')

# Only `dotnet test` with a fixed set of safe flags.
pattern='^dotnet test( --no-build| --no-restore| -c (Debug|Release)| --filter "[A-Za-z0-9._~=]+")*$'
if [[ "$cmd" =~ $pattern ]]; then
  filter='(^\s*failed |\[fail\]|error [A-Z]+[0-9]+|build failed|failed!|passed!|total tests|test run summary|^\s*(total|failed|succeeded|skipped):)'
  # If nothing matches, show the tail instead; always keep dotnet's exit code.
  filtered_cmd="out=\$($cmd 2>&1); rc=\$?; m=\$(echo \"\$out\" | grep -iE -A 8 '$filter'); if [ -n \"\$m\" ]; then echo \"\$m\" | head -150; else echo \"\$out\" | tail -60; fi; exit \$rc"
  echo "$input" | jq --arg filtered "$filtered_cmd" \
    '{hookSpecificOutput: {hookEventName: "PreToolUse", permissionDecision: "allow", permissionDecisionReason: "plain dotnet test, output filtered by hook", updatedInput: (.tool_input + {command: $filtered})}}'
else
  echo "{}"
fi
