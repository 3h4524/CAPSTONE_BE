#!/usr/bin/env bash
set -euo pipefail

: "${GH_TOKEN:?Set GH_TOKEN}"
: "${GITHUB_REPOSITORY:?Set GITHUB_REPOSITORY}"
: "${PR_NUMBER:?Set PR_NUMBER}"
: "${HEAD_SHA:?Set HEAD_SHA}"
: "${SONAR_TOKEN:?Set SONAR_TOKEN}"
: "${SONAR_PROJECT_KEY:?Set SONAR_PROJECT_KEY}"

host="${SONAR_HOST_URL:-https://sonarcloud.io}"
marker="<!-- apcs-sonar-check -->"
run_url="${GITHUB_SERVER_URL:-https://github.com}/$GITHUB_REPOSITORY/actions/runs/${GITHUB_RUN_ID:-}"
body="$(mktemp)"
trap 'rm -f "$body"' EXIT

sonar_get() {
  curl -fsS --retry 3 -H "Authorization: Bearer $SONAR_TOKEN" "$host/api/$1"
}

issues="$(sonar_get "issues/search?componentKeys=$SONAR_PROJECT_KEY&pullRequest=$PR_NUMBER&issueStatuses=OPEN,CONFIRMED&ps=100")"
hotspots="$(sonar_get "hotspots/search?projectKey=$SONAR_PROJECT_KEY&pullRequest=$PR_NUMBER&status=TO_REVIEW&ps=100")"
issue_count="$(jq -r .paging.total <<< "$issues")"
hotspot_count="$(jq -r .paging.total <<< "$hotspots")"
findings=$((issue_count + hotspot_count))
dashboard_url="$host/dashboard?id=$SONAR_PROJECT_KEY&pullRequest=$PR_NUMBER"

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "findings=$findings" >> "$GITHUB_OUTPUT"
fi

existing="$(gh api --paginate "repos/$GITHUB_REPOSITORY/issues/$PR_NUMBER/comments" \
  --jq ".[] | select(.user.login == \"github-actions[bot]\" and (.body | contains(\"$marker\"))) | .id" | tail -n 1)"

if [[ "$findings" -gt 0 ]]; then
  {
    echo "$marker"
    echo "### :x: Sonar check failed"
    echo
    echo "New code in commit \`${HEAD_SHA:0:7}\` has $issue_count open issue(s) and $hotspot_count hotspot(s) to review."
    echo
    echo "| Check (pull request) | Count |"
    echo "|---|---|"
    echo "| Open issues (all types) | $issue_count |"
    echo "| Security hotspots to review | $hotspot_count |"
    echo
    jq -r '.issues[] | "- :x: \(.type) \(.component | sub("^[^:]*:"; "")):\(.line // "-") \(.message) (`\(.rule)`)"' <<< "$issues"
    jq -r '.hotspots[] | "- :mag: \(.component | sub("^[^:]*:"; "")):\(.line // "-") \(.message) (`\(.ruleKey)`)"' <<< "$hotspots"
    echo
    echo "[SonarCloud dashboard]($dashboard_url) · [Workflow run]($run_url)"
  } > "$body"
elif [[ -n "$existing" ]]; then
  {
    echo "$marker"
    echo "### :white_check_mark: Sonar check passed"
    echo
    echo "No open issues or hotspots to review as of commit \`${HEAD_SHA:0:7}\`. [SonarCloud dashboard]($dashboard_url) · [Workflow run]($run_url)"
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
