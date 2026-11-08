#!/usr/bin/env bash
set -euo pipefail

deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
[ -f "$deploy_dir/.env" ] || { echo "Missing $deploy_dir/.env; run release.sh infra first" >&2; exit 1; }

compose() {
  docker compose -p apcs-dev --env-file "$deploy_dir/.env" -f "$deploy_dir/dev-db/compose.yml" "$@"
}

case "${1:-}" in
  up)
    docker volume inspect apcs_pg_dev > /dev/null 2>&1 || docker volume create apcs_pg_dev > /dev/null
    compose up -d --wait
    ;;
  restart) compose restart postgres-dev ;;
  status) compose ps ;;
  logs) compose logs --tail "${2:-100}" postgres-dev ;;
  *) echo "usage: dev-db.sh <up|restart|status|logs [lines]>" >&2; exit 2 ;;
esac
