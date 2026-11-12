#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: security-audit.sh audit <nuget:PROJECT|npm:DIR|pnpm:DIR>... | security-audit.sh trivyignore" >&2
  exit 2
}

acceptances="${SECURITY_ACCEPTANCES:-security-acceptances.json}"
summary="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
today="$(date -u +%F)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

active_ids() {
  [ -f "$acceptances" ] || return 0
  jq -r --arg today "$today" '.[] | select(.expires >= $today) | .ids[]' "$acceptances"
}

warn_expired() {
  [ -f "$acceptances" ] || return 0
  jq -r --arg today "$today" \
    '.[] | select(.expires < $today) | "::warning::The security acceptance for \(.package) (\(.ids | join(", "))) expired on \(.expires) and no longer applies."' \
    "$acceptances"
}

nuget_findings() {
  dotnet list "$1" package --vulnerable --include-transitive --format json > "$work/nuget.json" || true
  if ! jq -e '.projects' "$work/nuget.json" > /dev/null 2>&1; then
    echo "::error::dotnet list did not return a vulnerability report for $1"
    cat "$work/nuget.json" >&2
    return 1
  fi
  if jq -e '[.problems // [] | .[] | select(.level == "error")] | length > 0' "$work/nuget.json" > /dev/null; then
    jq -r '.problems[] | select(.level == "error") | "::error::\(.text)"' "$work/nuget.json"
    return 1
  fi
  jq -r '[.projects[].frameworks[]? | (.topLevelPackages // [])[], (.transitivePackages // [])[]
      | . as $package | (.vulnerabilities // [])[]
      | select(.severity == "High" or .severity == "Critical")
      | [(.advisoryurl | split("/") | last), (.severity | ascii_downcase), "\($package.id) \($package.resolvedVersion)", .advisoryurl]
      | @tsv] | unique[]' "$work/nuget.json"
}

npm_findings() {
  (cd "$1" && npm audit --omit=dev --json || true) > "$work/npm.json"
  if ! jq -e '.vulnerabilities' "$work/npm.json" > /dev/null 2>&1; then
    echo "::error::npm audit did not return a report for $1"
    cat "$work/npm.json" >&2
    return 1
  fi
  jq -r '[.vulnerabilities[] | .via[] | objects
      | select(.severity == "high" or .severity == "critical")
      | [(.url | split("/") | last), .severity, "\(.name) \(.range)", .url]
      | @tsv] | unique[]' "$work/npm.json"
}

pnpm_findings() {
  (cd "$1" && pnpm audit --prod --json || true) > "$work/pnpm.json"
  if ! jq -e '.metadata' "$work/pnpm.json" > /dev/null 2>&1; then
    echo "::error::pnpm audit did not return a report for $1"
    cat "$work/pnpm.json" >&2
    return 1
  fi
  jq -r '[.advisories // {} | .[]
      | select(.severity == "high" or .severity == "critical")
      | [.github_advisory_id, .severity, "\(.module_name) \(.vulnerable_versions)", .url]
      | @tsv] | unique[]' "$work/pnpm.json"
}

audit() {
  [ "$#" -gt 0 ] || usage
  warn_expired
  for spec in "$@"; do
    kind="${spec%%:*}"
    case "$kind" in
      nuget | npm | pnpm) "${kind}_findings" "${spec#*:}" >> "$work/findings.tsv" ;;
      *) usage ;;
    esac
  done
  active_ids > "$work/accepted.txt"

  blocked=0
  {
    echo "## Dependency audit (high and critical)"
    echo
    if [ ! -s "$work/findings.tsv" ]; then
      echo "No high or critical advisories."
    else
      echo "| Status | Severity | Package | Advisory |"
      echo "|---|---|---|---|"
    fi
  } >> "$summary"
  while IFS=$'\t' read -r id severity package url; do
    if grep -qxF "$id" "$work/accepted.txt"; then
      status=accepted
    else
      status=blocked
      blocked=$((blocked + 1))
    fi
    echo "| $status | $severity | $package | [$id]($url) |" >> "$summary"
  done < <(sort -u "$work/findings.tsv")

  if [ "$blocked" -gt 0 ]; then
    echo "::error::$blocked high/critical advisory(ies) are not fixed or accepted. Upgrade the package, or add an entry with a reason and an expiry date to $acceptances."
    exit 1
  fi
  echo "Dependency audit passed."
}

case "${1:-}" in
  audit) shift; audit "$@" ;;
  trivyignore) active_ids ;;
  *) usage ;;
esac
