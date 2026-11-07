#!/usr/bin/env bash
set -euo pipefail

source="${1:-latest}"
name="apcs-restore-test-$$"

if [ "$source" = latest ]; then
  dump="$(docker run --rm -v apcs_backups:/backups:ro alpine:3 sh -c 'ls -1d /backups/daily/*/ | sort | tail -n 1')prod.dump"
else
  dump="/backups/$source"
fi

cleanup() { docker rm -f "$name" > /dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$name" -e POSTGRES_PASSWORD=restore-test -v apcs_backups:/backups:ro pgvector/pgvector:pg18 > /dev/null
for _ in $(seq 1 30); do
  docker exec "$name" pg_isready -U postgres > /dev/null 2>&1 && break
  sleep 2
done

docker exec "$name" psql -q -U postgres -c 'CREATE DATABASE apcs;'
docker exec "$name" psql -q -U postgres -d apcs -c 'CREATE EXTENSION IF NOT EXISTS vector; CREATE EXTENSION IF NOT EXISTS "uuid-ossp";'
docker exec "$name" pg_restore -U postgres -d apcs --no-owner --no-privileges "$dump" || true

tables="$(docker exec "$name" psql -tA -U postgres -d apcs -c "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';")"
users="$(docker exec "$name" psql -tA -U postgres -d apcs -c 'SELECT count(*) FROM users;')"
[ "$tables" -gt 0 ] || { echo "Restore test FAILED: no tables restored from $dump" >&2; exit 1; }

message="Restore test OK: $dump -> $tables tables, $users users"
echo "$message"
if [ -n "${DISCORD_WEBHOOK_URL:-}" ]; then
  curl -fsS -m 15 -H 'Content-Type: application/json' -d "{\"content\":\"🧪 $message\"}" "$DISCORD_WEBHOOK_URL" > /dev/null || true
fi
