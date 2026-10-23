#!/bin/bash
# PreToolUse hook: when Claude runs a plain `dotnet test`, rewrite the command
# so only failures, build errors and the summary reach the context.
input=$(cat)
cmd=$(echo "$input" | jq -r '.tool_input.command // empty')

# Only plain `dotnet test [args]` without pipes, redirects or chaining.
pattern='^dotnet test( [^;&|<>`$#\\[:cntrl:]]*)?$'
if [[ "$cmd" =~ $pattern ]]; then
  filtered_cmd="set -o pipefail; $cmd 2>&1 | grep -iE -A 8 '(^\s*failed |\[fail\]|error [A-Z]+[0-9]+|build failed|failed!|passed!|total tests|test run summary|^\s*(total|failed|succeeded|skipped):)' | head -150"
  echo "$input" | jq --arg filtered "$filtered_cmd" \
    '{hookSpecificOutput: {hookEventName: "PreToolUse", permissionDecision: "allow", permissionDecisionReason: "plain dotnet test, output filtered by hook", updatedInput: (.tool_input + {command: $filtered})}}'
else
  echo "{}"
fi
