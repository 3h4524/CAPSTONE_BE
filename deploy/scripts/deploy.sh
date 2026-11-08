#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: deploy.sh <api|fe> <tag>" >&2
  exit 2
}

[ $# -eq 2 ] || usage
target="$1"
tag="$2"
deploy_dir="$(cd "$(dirname "$0")/.." && pwd)"
releases_dir="${RELEASES_DIR:-/opt/apcs/releases}"
cd "$deploy_dir"

case "$target" in
  api) variable=API_TAG; services=(api media-worker); probe=api ;;
  fe) variable=FE_TAG; services=(fe); probe=fe ;;
  *) usage ;;
esac

[ -f .env ] || { echo "Missing $deploy_dir/.env (copy .env.example)" >&2; exit 1; }
mkdir -p "$releases_dir"

set_tag() {
  if grep -qE "^$variable=" .env; then
    sed -i "s|^$variable=.*|$variable=$1|" .env
  else
    echo "$variable=$1" >> .env
  fi
}

wait_healthy() {
  local container status
  container="$(docker compose ps -q "$probe")"
  for _ in $(seq 1 36); do
    status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container" 2> /dev/null || echo missing)"
    case "$status" in
      healthy) return 0 ;;
      unhealthy|exited|dead|missing) return 1 ;;
    esac
    sleep 5
  done
  return 1
}

previous="$(grep -E "^$variable=" .env | cut -d= -f2- || true)"
echo "Deploying $target: ${previous:-none} -> $tag"

set_tag "$tag"
docker compose pull "${services[@]}"
docker compose up -d --no-deps "${services[@]}"

if wait_healthy; then
  [ -n "$previous" ] && [ "$previous" != "$tag" ] && echo "$previous" > "$releases_dir/$target.prev"
  echo "$tag" > "$releases_dir/$target.current"
  echo "Deployed $target $tag"
  exit 0
fi

echo "$probe did not become healthy; rolling back to ${previous:-nothing}" >&2
docker compose logs --tail 80 "$probe" >&2 || true
if [ -n "$previous" ] && [ "$previous" != "$tag" ]; then
  set_tag "$previous"
  docker compose up -d --no-deps "${services[@]}"
fi
exit 1
