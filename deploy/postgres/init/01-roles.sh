#!/usr/bin/env bash
set -euo pipefail

: "${APCS_DB_FLAVOR:?Set APCS_DB_FLAVOR to prod or dev}"
: "${APCS_MIGRATOR_PASSWORD:?Set APCS_MIGRATOR_PASSWORD}"
: "${APCS_BACKUP_PASSWORD:?Set APCS_BACKUP_PASSWORD}"

case "$APCS_DB_FLAVOR" in
  prod)
    : "${APCS_APP_PASSWORD:?Set APCS_APP_PASSWORD}"
    runtime_role=apcs_app
    runtime_login=on
    ;;
  dev)
    runtime_role=dev_rw
    runtime_login=off
    APCS_APP_PASSWORD=""
    ;;
  *)
    echo "APCS_DB_FLAVOR must be prod or dev" >&2
    exit 1
    ;;
esac

psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v runtime_role="$runtime_role" \
  -v runtime_login="$runtime_login" \
  -v migrator_password="$APCS_MIGRATOR_PASSWORD" \
  -v app_password="$APCS_APP_PASSWORD" \
  -v backup_password="$APCS_BACKUP_PASSWORD" <<'SQL'
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE ROLE apcs_migrator LOGIN PASSWORD :'migrator_password';
CREATE ROLE apcs_backup LOGIN PASSWORD :'backup_password';
GRANT pg_read_all_data TO apcs_backup;

\if :runtime_login
CREATE ROLE :"runtime_role" LOGIN PASSWORD :'app_password';
\else
CREATE ROLE :"runtime_role" NOLOGIN;
\endif

ALTER DATABASE :"db" OWNER TO apcs_migrator;
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE :"db" TO :"runtime_role", apcs_backup;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO :"runtime_role", apcs_backup;

ALTER DEFAULT PRIVILEGES FOR ROLE apcs_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE apcs_migrator IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE apcs_migrator IN SCHEMA public
  GRANT EXECUTE ON FUNCTIONS TO :"runtime_role";
SQL
