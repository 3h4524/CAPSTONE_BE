#!/usr/bin/env bash
set -euo pipefail

export -p | grep -E ' (PG|REDIS|BACKUP|RCLONE|DISCORD|TZ)[A-Z_]*=' > /etc/backup.env
chmod 600 /etc/backup.env

exec "$@"
