#!/usr/bin/env bash
set -euo pipefail

[ "${APCS_DB_FLAVOR:-}" = dev ] || exit 0

psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" <<'SQL'
CREATE SCHEMA IF NOT EXISTS apcs_ops;
REVOKE ALL ON SCHEMA apcs_ops FROM PUBLIC;
CREATE OR REPLACE FUNCTION apcs_ops.apcs_guard_dev_ddl() RETURNS event_trigger
LANGUAGE plpgsql AS $fn$
BEGIN
  IF session_user::text LIKE 'dev\_%' AND coalesce(current_setting('application_name', true), '') NOT LIKE 'Flyway%' THEN
    RAISE EXCEPTION 'Schema changes on the shared dev database must go through Flyway migrations (% refused).', tg_tag
      USING ERRCODE = 'insufficient_privilege',
            HINT = 'Write the SQL in migrations/ with ./scripts/New-Migration.ps1 -Name <what_it_does>, then apply it with ./scripts/Migrate-DevDb.ps1 -Scaffold. See "Changing the database schema" in AGENTS.md.';
  END IF;
END
$fn$;
DROP EVENT TRIGGER IF EXISTS apcs_guard_dev_ddl;
CREATE EVENT TRIGGER apcs_guard_dev_ddl ON ddl_command_start EXECUTE FUNCTION apcs_ops.apcs_guard_dev_ddl();
SQL
