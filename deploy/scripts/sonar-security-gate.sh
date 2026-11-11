#!/usr/bin/env bash
set -euo pipefail

report_task="${1:?Usage: sonar-security-gate.sh <report-task.txt>}"
: "${SONAR_TOKEN:?Set SONAR_TOKEN}"
host="${SONAR_HOST_URL:-https://sonarcloud.io}"
summary="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

sonar_get() {
  curl -fsS --retry 3 -H "Authorization: Bearer $SONAR_TOKEN" "$host/api/$1"
}

report_value() {
  sed -n "s/^$1=//p" "$report_task"
}

project_key="$(report_value projectKey)"
ce_task_id="$(report_value ceTaskId)"
dashboard_url="$(report_value dashboardUrl)"

status=PENDING
for _ in $(seq 1 120); do
  status="$(sonar_get "ce/task?id=$ce_task_id" | jq -r .task.status)"
  case "$status" in
    SUCCESS) break ;;
    FAILED | CANCELED)
      echo "::error::Sonar background task $ce_task_id ended as $status"
      exit 1
      ;;
  esac
  sleep 5
done
if [ "$status" != SUCCESS ]; then
  echo "::error::Timed out waiting for Sonar background task $ce_task_id"
  exit 1
fi

issues="$(sonar_get "issues/search?componentKeys=$project_key&inNewCodePeriod=true&impactSoftwareQualities=SECURITY&impactSeverities=LOW,MEDIUM,HIGH,BLOCKER&issueStatuses=OPEN,CONFIRMED&ps=20")"
hotspots="$(sonar_get "hotspots/search?project=$project_key&inNewCodePeriod=true&status=TO_REVIEW&ps=20")"
gate="$(sonar_get "qualitygates/project_status?projectKey=$project_key")"

issue_count="$(jq -r .paging.total <<< "$issues")"
hotspot_count="$(jq -r .paging.total <<< "$hotspots")"

{
  echo "## Sonar security gate"
  echo
  echo "| Check (new code) | Count |"
  echo "|---|---|"
  echo "| Open security issues | $issue_count |"
  echo "| Security hotspots to review | $hotspot_count |"
  echo
  echo "Quality gate (informational): **$(jq -r .projectStatus.status <<< "$gate")**"
  jq -r '.projectStatus.conditions[] | select(.status == "ERROR") | "- :warning: `\(.metricKey)` = \(.actualValue) (threshold \(.comparator) \(.errorThreshold))"' <<< "$gate"
  echo
  jq -r '.issues[] | "- :x: \(.component | sub("^[^:]*:"; "")):\(.line // "-") \(.message) (`\(.rule)`)"' <<< "$issues"
  jq -r '.hotspots[] | "- :mag: \(.component | sub("^[^:]*:"; "")):\(.line // "-") \(.message) (`\(.ruleKey)`)"' <<< "$hotspots"
  echo
  echo "Dashboard: $dashboard_url"
} >> "$summary"

if [ "$issue_count" -gt 0 ] || [ "$hotspot_count" -gt 0 ]; then
  echo "::error::Sonar found $issue_count security issue(s) and $hotspot_count unreviewed hotspot(s) in new code. Fix them or review them in Sonar, then tag again."
  exit 1
fi
echo "Sonar security gate passed."
