#!/usr/bin/env bash
set -euo pipefail

[ $# -eq 1 ] || { echo "usage: restore-keyring.sh <keyring.tgz>" >&2; exit 2; }
archive="$(realpath "$1")"
cd "$(dirname "$0")/.."

docker compose stop api
docker run --rm -v apcs_keyring:/keyring -v "$(dirname "$archive"):/in:ro" alpine:3 \
  sh -c "tar xzf '/in/$(basename "$archive")' -C /keyring && chown -R 1654:1654 /keyring"
docker compose up -d api
echo "Key ring restored from $archive"
