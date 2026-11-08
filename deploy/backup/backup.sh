#!/usr/bin/env bash
set -Eeuo pipefail

if [ -f /etc/backup.env ]; then
  . /etc/backup.env
fi

only=all
label=""
while [ $# -gt 0 ]; do
  case "$1" in
    --only) only="$2"; shift 2 ;;
    --label) label="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

root=/backups
stamp="$(date +%Y%m%d-%H%M%S)"
keep_daily="${BACKUP_KEEP_DAILY_DAYS:-7}"
keep_weekly="${BACKUP_KEEP_WEEKLY_DAYS:-28}"
step="start"

if [ -n "$label" ]; then
  kind=predeploy
  target="$root/predeploy/$stamp-$label"
else
  kind=daily
  target="$root/daily/$stamp"
fi
mkdir -p "$target"

notify() {
  [ -n "${DISCORD_WEBHOOK_URL:-}" ] || return 0
  local payload
  payload="$(printf '{"content":"%s"}' "$1")"
  curl -fsS -m 15 -H 'Content-Type: application/json' -d "$payload" "$DISCORD_WEBHOOK_URL" > /dev/null || true
}

on_error() {
  echo "Backup failed at step: $step" >&2
  notify "❌ APCS backup ($kind $stamp) failed at step: $step"
}
trap on_error ERR

dump_database() {
  local name="$1" host="$2" password="$3"
  step="pg_dump $name"
  PGPASSWORD="$password" pg_dump -h "$host" -U apcs_backup -d apcs -Fc -f "$target/$name.dump"
  step="verify $name"
  pg_restore --list "$target/$name.dump" > /dev/null
}

if [ "$only" = all ] || [ "$only" = prod ]; then
  dump_database prod postgres-prod "$PGPASSWORD_PROD"
fi

if [ "$only" = all ] || [ "$only" = dev ]; then
  dump_database dev postgres-dev "$PGPASSWORD_DEV"
fi

if [ "$only" = all ] || [ "$only" = prod ]; then
  step="keyring"
  tar czf "$target/keyring.tgz" -C /keyring .
  step="redis"
  redis-cli -h redis -a "$REDIS_PASSWORD" --no-auth-warning --rdb "$target/redis.rdb" > /dev/null
fi

step="checksum"
(cd "$target" && sha256sum -- * > SHA256SUMS)

if [ "$kind" = daily ] && [ "$(date +%u)" = 7 ]; then
  step="weekly copy"
  mkdir -p "$root/weekly"
  cp -a "$target" "$root/weekly/"
fi

step="local retention"
find "$root/daily" -mindepth 1 -maxdepth 1 -type d -mtime +"$keep_daily" -exec rm -rf {} + 2> /dev/null || true
find "$root/weekly" -mindepth 1 -maxdepth 1 -type d -mtime +"$keep_weekly" -exec rm -rf {} + 2> /dev/null || true
if [ -d "$root/predeploy" ]; then
  ls -1dt "$root"/predeploy/*/ 2> /dev/null | tail -n +11 | xargs -r rm -rf
fi

for remote in ${BACKUP_REMOTES:-}; do
  if [ "${remote%%:*}" = gdrive ] && [ -z "${RCLONE_CONFIG_GDRIVE_TOKEN:-}" ]; then
    echo "Skipping $remote: no Google Drive token configured"
    continue
  fi
  step="upload $remote"
  rclone copy "$root" "$remote" --transfers 2 --no-traverse
  step="remote retention $remote"
  rclone delete "$remote/daily" --min-age "$((keep_daily + 1))d" --rmdirs 2> /dev/null || true
  rclone delete "$remote/weekly" --min-age "$((keep_weekly + 1))d" --rmdirs 2> /dev/null || true
  rclone delete "$remote/predeploy" --min-age 30d --rmdirs 2> /dev/null || true
done

size="$(du -sh "$target" | cut -f1)"
echo "Backup $kind $stamp done ($size) -> ${BACKUP_REMOTES:-local only}"
notify "✅ APCS backup ($kind $stamp) done: $size -> ${BACKUP_REMOTES:-local only}"
