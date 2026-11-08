#!/usr/bin/env bash
set -euo pipefail

[ $# -eq 1 ] || { echo "usage: rollback.sh <api|fe>" >&2; exit 2; }
releases_dir="${RELEASES_DIR:-/opt/apcs/releases}"
previous_file="$releases_dir/$1.prev"
[ -s "$previous_file" ] || { echo "No previous release recorded for $1" >&2; exit 1; }

exec "$(dirname "$0")/deploy.sh" "$1" "$(cat "$previous_file")"
