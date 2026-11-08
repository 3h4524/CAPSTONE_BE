#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: release.sh fe <tag> | api <tag> | infra" >&2
  exit 2
}

target="${1:-}"
tag="${2:-}"
case "$target" in
  fe|api) [ -n "$tag" ] || usage ;;
  infra) ;;
  *) usage ;;
esac

deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
cd "$deploy_dir"

lock_file="${RELEASES_DIR:-/opt/apcs/releases}/.release.lock"
exec 9> "$lock_file"
if ! flock -n 9; then
  echo "Another release is running; waiting for it to finish"
  flock 9
fi

ensure_data_services() {
  docker compose up -d --no-recreate postgres-prod redis redis-media
}

ensure_backup_image() {
  docker image inspect apcs-backup:local > /dev/null 2>&1 || docker compose build backup
}

case "$target" in
  fe)
    [ -f .env ] || { echo "Missing $deploy_dir/.env; run release.sh infra first" >&2; exit 1; }
    "$deploy_dir/scripts/deploy.sh" fe "$tag"
    ;;
  api)
    "$deploy_dir/scripts/render-env.sh"
    ensure_data_services
    ensure_backup_image
    docker compose run --rm --no-deps backup /usr/local/bin/backup.sh --only prod --label "$tag"
    "$deploy_dir/scripts/migrate.sh" prod migrate
    "$deploy_dir/scripts/deploy.sh" api "$tag"
    ;;
  infra)
    "$deploy_dir/scripts/render-env.sh"
    ensure_data_services
    docker compose build backup
    docker compose up -d --no-deps dozzle uptime-kuma backup
    if [ -n "$(docker compose ps -q caddy)" ]; then
      docker compose up -d --no-deps caddy
      if docker compose exec -T caddy cmp -s - /etc/caddy/Caddyfile < Caddyfile; then
        docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
      else
        echo "Caddy still mounts a replaced Caddyfile; restarting it to pick up the new file"
        docker compose restart caddy
      fi
    else
      docker compose up -d --no-deps caddy
    fi
    ;;
esac

docker image prune -f > /dev/null
echo "Release $target ${tag:-} done"
