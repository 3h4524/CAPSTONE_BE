#!/usr/bin/env bash
set -euo pipefail

[ $# -eq 2 ] || { echo "usage: release.sh <api|fe> <tag>" >&2; exit 2; }
target="$1"
tag="$2"
deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$deploy_dir"

lock_file="${RELEASES_DIR:-/opt/apcs/releases}/.release.lock"
exec 9> "$lock_file"
if ! flock -n 9; then
  echo "Another release is running; waiting for it to finish"
  flock 9
fi

"$deploy_dir/scripts/render-env.sh"
docker compose up -d postgres-prod postgres-dev redis dozzle uptime-kuma
docker compose build backup
docker compose up -d backup

if [ "$target" = api ]; then
  docker compose run --rm backup /usr/local/bin/backup.sh --only prod --label "$tag"
  "$deploy_dir/scripts/migrate.sh" prod migrate
fi

"$deploy_dir/scripts/deploy.sh" "$target" "$tag"
docker compose up -d --no-deps caddy
docker image prune -f > /dev/null
echo "Release $target $tag done"
