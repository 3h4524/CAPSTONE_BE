#!/usr/bin/env bash
set -euo pipefail

case "${1:-}" in
  prod|dev) environment="$1"; shift ;;
  *) echo "usage: migrate.sh <prod|dev> [flyway command, default migrate]" >&2; exit 2 ;;
esac

deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
env_dir="${ENV_DIR:-/opt/apcs/env}"
migrations_dir="${MIGRATIONS_DIR:-/opt/apcs/migrations}"
password="$(grep -E '^APCS_MIGRATOR_PASSWORD=' "$env_dir/postgres-$environment.env" | cut -d= -f2-)"
[ -n "$password" ] || { echo "No migrator password in $env_dir/postgres-$environment.env" >&2; exit 1; }

docker run --rm --network apcs_apcs \
  -e FLYWAY_URL="jdbc:postgresql://postgres-$environment:5432/apcs" \
  -e FLYWAY_USER=apcs_migrator \
  -e FLYWAY_PASSWORD="$password" \
  -v "$migrations_dir:/flyway/sql:ro" \
  -v "$deploy_dir/flyway/flyway.toml:/flyway/conf/flyway.toml:ro" \
  "${FLYWAY_IMAGE:-flyway/flyway:11}" "${@:-migrate}"
