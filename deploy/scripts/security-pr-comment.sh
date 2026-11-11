#!/usr/bin/env bash
set -euo pipefail

: "${GH_TOKEN:?Set GH_TOKEN}"
: "${GITHUB_REPOSITORY:?Set GITHUB_REPOSITORY}"
: "${PR_NUMBER:?Set PR_NUMBER}"
: "${HEAD_SHA:?Set HEAD_SHA}"
audit_outcome="${AUDIT_OUTCOME:-skipped}"
trivy_outcome="${TRIVY_OUTCOME:-skipped}"
audit_report="${AUDIT_REPORT:-/tmp/security-audit.md}"
trivy_report="${TRIVY_REPORT:-/tmp/security-trivy.md}"
run_url="${GITHUB_SERVER_URL:-https://github.com}/$GITHUB_REPOSITORY/actions/runs/${GITHUB_RUN_ID:-}"
marker="<!-- apcs-security-check -->"
body="$(mktemp)"
trap 'rm -f "$body"' EXIT

existing="$(gh api --paginate "repos/$GITHUB_REPOSITORY/issues/$PR_NUMBER/comments" \
  --jq ".[] | select(.user.login == \"github-actions[bot]\" and (.body | contains(\"$marker\"))) | .id" | tail -n 1)"

if [[ "$audit_outcome" == failure || "$trivy_outcome" == failure ]]; then
  {
    echo "$marker"
    echo "### :x: Security check failed"
    echo
    echo "Commit \`${HEAD_SHA:0:7}\` has high or critical findings. A release with them will be blocked."
    echo "Upgrade the package or base image. If the risk is accepted, add an entry with a reason and an expiry date to \`security-acceptances.json\`."
    echo
    if [[ "$audit_outcome" == failure && -s "$audit_report" ]]; then
      cat "$audit_report"
      echo
    fi
    if [[ "$trivy_outcome" == failure && -s "$trivy_report" ]]; then
      cat "$trivy_report"
      echo
    fi
    echo "[Workflow run]($run_url)"
  } > "$body"
elif [[ -n "$existing" ]]; then
  {
    echo "$marker"
    echo "### :white_check_mark: Security check passed"
    echo
    echo "The findings reported earlier are resolved as of commit \`${HEAD_SHA:0:7}\`. [Workflow run]($run_url)"
  } > "$body"
else
  echo "No findings and no earlier report, so nothing to post."
  exit 0
fi

if [[ -n "$existing" ]]; then
  gh api -X PATCH "repos/$GITHUB_REPOSITORY/issues/comments/$existing" -F "body=@$body" --jq .html_url
else
  gh api -X POST "repos/$GITHUB_REPOSITORY/issues/$PR_NUMBER/comments" -F "body=@$body" --jq .html_url
fi
