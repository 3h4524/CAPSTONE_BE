#!/usr/bin/env bash
set -euo pipefail

: "${FLYWAY_URL:?Set FLYWAY_URL, e.g. jdbc:postgresql://127.0.0.1:15432/apcs}"
: "${FLYWAY_USER:?Set FLYWAY_USER}"
: "${FLYWAY_PASSWORD:?Set FLYWAY_PASSWORD}"
[ $# -gt 0 ] || { echo "usage: flyway.sh <migrate|info|validate|baseline> [flyway args]" >&2; exit 2; }

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
docker run --rm --network host \
  -e FLYWAY_URL -e FLYWAY_USER -e FLYWAY_PASSWORD \
  -v "$repo_root/migrations:/flyway/sql:ro" \
  -v "$repo_root/deploy/flyway/flyway.toml:/flyway/conf/flyway.toml:ro" \
  "${FLYWAY_IMAGE:-flyway/flyway:11}" "$@"
