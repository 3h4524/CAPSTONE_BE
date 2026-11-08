#!/usr/bin/env bash
set -euo pipefail

[ $# -eq 2 ] || { echo "usage: restore-db.sh <prod|dev> <file.dump>" >&2; exit 2; }
environment="$1"
dump="$(realpath "$2")"
[ -f "$dump" ] || { echo "No such file: $dump" >&2; exit 1; }

case "$environment" in
  prod) service=postgres-prod ;;
  dev) service=postgres-dev ;;
  *) echo "environment must be prod or dev" >&2; exit 2 ;;
esac

cd "$(dirname "$0")/.."
read -r -p "This replaces every object in the $environment database with $dump. Type '$environment' to continue: " answer
[ "$answer" = "$environment" ] || { echo "Cancelled"; exit 1; }

if [ "$environment" = prod ]; then
  docker compose stop api media-worker
fi

docker compose exec -T "$service" pg_restore -U apcs_migrator -d apcs --clean --if-exists --no-owner --no-privileges \
  < "$dump" || echo "pg_restore reported errors (existing extensions are expected); review the output above" >&2

"$(dirname "$0")/db-grants.sh" "$environment"

if [ "$environment" = prod ]; then
  docker compose up -d api media-worker
fi
echo "Restored $environment from $dump"
