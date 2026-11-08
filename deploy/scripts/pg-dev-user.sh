#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "usage: pg-dev-user.sh <add|remove> <name> | list" >&2
  exit 2
}

cd "$(dirname "$0")/.."
action="${1:-}"

psql_dev() {
  docker compose exec -T postgres-dev psql -v ON_ERROR_STOP=1 -U postgres -d apcs "$@"
}

case "$action" in
  list)
    psql_dev -c "SELECT r.rolname AS login, r.rolconnlimit AS connection_limit
                 FROM pg_roles r JOIN pg_auth_members m ON m.member = r.oid
                 JOIN pg_roles g ON g.oid = m.roleid
                 WHERE g.rolname = 'dev_rw' ORDER BY 1;"
    exit 0
    ;;
  add|remove) ;;
  *) usage ;;
esac

name="${2:-}"
[[ "$name" =~ ^[a-z][a-z0-9_]{1,30}$ ]] || { echo "name must match ^[a-z][a-z0-9_]{1,30}$" >&2; exit 2; }
role="dev_$name"

if [ "$action" = remove ]; then
  psql_dev -v role="$role" <<'SQL'
DROP ROLE IF EXISTS :"role";
SQL
  echo "Removed $role"
  exit 0
fi

password="$(openssl rand -base64 32 | tr -dc 'A-Za-z0-9' | cut -c1-28)"
psql_dev -v role="$role" -v password="$password" <<'SQL'
CREATE ROLE :"role" LOGIN PASSWORD :'password' IN ROLE dev_rw CONNECTION LIMIT 15;
SQL

host="${DEV_DB_HOST:-<vm-host>}"
cat <<INFO
Created $role. Send this privately, it is shown only once:
ConnectionStrings__DefaultConnection=Host=$host;Port=5432;Database=apcs;Username=$role;Password=$password;SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=10
INFO
