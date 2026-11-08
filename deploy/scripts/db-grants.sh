#!/usr/bin/env bash
set -euo pipefail

case "${1:-}" in
  prod) service=postgres-prod; role=apcs_app ;;
  dev) service=postgres-dev; role=dev_rw ;;
  *) echo "usage: db-grants.sh <prod|dev>" >&2; exit 2 ;;
esac

cd "$(dirname "$0")/.."
docker compose exec -T "$service" psql -v ON_ERROR_STOP=1 -U postgres -d apcs -v runtime_role="$role" \
  < postgres/grant-runtime.sql
echo "Granted runtime privileges to $role on $service"
