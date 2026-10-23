#!/usr/bin/env bash
# Starts a throwaway PostgreSQL in Docker and fills it with revenue demo data.
#
#   ./scripts/demo/revenue-demo.sh up       start the database, load schema + demo data (idempotent)
#   ./scripts/demo/revenue-demo.sh reseed   delete the demo customers and generate them again
#   ./scripts/demo/revenue-demo.sh down     stop the container and delete its data
#
# Safety: every write goes through `docker exec` into the demo container, so this script cannot
# write to the team database. The team database is only read, inside a read-only transaction, to
# copy the plans, the roles and the admin accounts, so admins can sign in with their usual password
# and the plan names and prices match production.
#
# Run it from Git Bash on Windows.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCHEMA_SQL="${REPO_ROOT}/scripts/sql/000-bootstrap-schema.sql"
SEED_SQL="${REPO_ROOT}/scripts/demo/seed-revenue-demo.sql"

CONTAINER="apcs-revenue-demo"
VOLUME="apcs_revenue_demo_data"
PORT="${DEMO_PG_PORT:-55460}"
DB="apcs"
DB_USER="apcs"
DB_PASSWORD="apcs"
IMAGE="postgres:18-alpine"
DEMO_CONNECTION="Host=localhost;Port=${PORT};Database=${DB};Username=${DB_USER};Password=${DB_PASSWORD}"

say() { printf '\033[1;34m[demo]\033[0m %s\n' "$*"; }
ok()  { printf '   \033[32m%s\033[0m\n' "$*"; }
die() { printf '   \033[1;31mFATAL\033[0m %s\n' "$*" >&2; exit 1; }

# Git Bash rewrites arguments that look like paths; nothing here is a host path.
export MSYS_NO_PATHCONV=1

demo_psql() {
  docker exec -i "${CONTAINER}" psql -v ON_ERROR_STOP=1 -q -U "${DB_USER}" -d "${DB}" "$@"
}

demo_scalar() {
  demo_psql -tAX -c "$1" | tr -d '[:space:]'
}

start_container() {
  docker info >/dev/null 2>&1 || die "Docker is not running. Open Docker Desktop and wait for the engine."

  if [ -z "$(docker ps -aq -f "name=^${CONTAINER}$")" ]; then
    say "starting ${CONTAINER} on port ${PORT}"
    docker run -d --name "${CONTAINER}" \
      -e POSTGRES_USER="${DB_USER}" -e POSTGRES_PASSWORD="${DB_PASSWORD}" -e POSTGRES_DB="${DB}" \
      -p "${PORT}:5432" -v "${VOLUME}:/var/lib/postgresql" "${IMAGE}" >/dev/null
  else
    docker start "${CONTAINER}" >/dev/null
  fi

  for _ in $(seq 1 60); do
    if docker exec "${CONTAINER}" pg_isready -U "${DB_USER}" -d "${DB}" >/dev/null 2>&1; then
      ok "PostgreSQL is ready"
      return
    fi
    sleep 1
  done
  die "PostgreSQL did not become ready; see: docker logs ${CONTAINER}"
}

load_schema() {
  local tables
  tables="$(demo_scalar "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'")"
  if [ "${tables}" -gt 0 ]; then
    ok "schema already loaded (${tables} tables)"
    return
  fi
  say "loading schema"
  # pg_dump 18 wraps the dump in \restrict/\unrestrict, which the server cannot parse.
  grep -v '^\\' "${SCHEMA_SQL}" | demo_psql >/dev/null
  ok "schema loaded"
}

# Reads ConnectionStrings__DefaultConnection from .env without ever printing it.
team_db_part() {
  local cs
  cs="$(grep '^ConnectionStrings__DefaultConnection=' "${REPO_ROOT}/.env" | cut -d= -f2- | tr -d '"\r')"
  printf '%s' "${cs}" | tr ';' '\n' | grep -i "^$1=" | head -n1 | cut -d= -f2-
}

copy_from_team_db() {
  local query="$1" target="$2"
  local host db user password
  host="$(team_db_part Host)"; db="$(team_db_part Database)"
  user="$(team_db_part Username)"; [ -n "${user}" ] || user="$(team_db_part 'User Id')"
  password="$(team_db_part Password)"

  docker run --rm -i -e PGPASSWORD="${password}" -e PGSSLMODE=require "${IMAGE}" \
    psql -v ON_ERROR_STOP=1 -q -h "${host}" -U "${user}" -d "${db}" \
      -c "SET default_transaction_read_only = on" \
      -c "\\copy (${query}) TO STDOUT" \
    | demo_psql -c "\\copy ${target} FROM STDIN"
}

copy_reference_data() {
  if [ "$(demo_scalar "SELECT count(*) FROM roles")" -gt 0 ]; then
    ok "plans, roles and admin accounts already copied"
    return
  fi
  if [ ! -f "${REPO_ROOT}/.env" ]; then
    say "no .env found: using default plans and no admin account (sign-in will not work)"
    return
  fi

  say "copying plans, roles and admin accounts from the team database (read-only)"
  copy_from_team_db \
    "SELECT id, code, name, description, is_system_role, created_at FROM roles" \
    "roles (id, code, name, description, is_system_role, created_at)"
  copy_from_team_db \
    "SELECT id, name, tier, description, monthly_price_usd, annual_price_usd, max_batch_size, max_products_per_month, max_concurrent_jobs, image_generation_quota, video_generation_quota, api_call_quota, storage_quota_gb, priority_support, custom_api_keys_allowed, is_active, sort_order, created_at, updated_at FROM subscription_plans" \
    "subscription_plans (id, name, tier, description, monthly_price_usd, annual_price_usd, max_batch_size, max_products_per_month, max_concurrent_jobs, image_generation_quota, video_generation_quota, api_call_quota, storage_quota_gb, priority_support, custom_api_keys_allowed, is_active, sort_order, created_at, updated_at)"
  copy_from_team_db \
    "SELECT DISTINCT u.id, u.email, u.password_hash, u.full_name, u.oauth_google_id, u.oauth_provider, u.avatar_url, u.account_status, u.email_verified, u.email_verified_at, u.created_at, u.updated_at FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id WHERE r.code = 'Admin' AND ur.revoked_at IS NULL AND u.deleted_at IS NULL" \
    "users (id, email, password_hash, full_name, oauth_google_id, oauth_provider, avatar_url, account_status, email_verified, email_verified_at, created_at, updated_at)"
  copy_from_team_db \
    "SELECT ur.user_id, ur.role_id, ur.granted_at FROM user_roles ur JOIN roles r ON r.id = ur.role_id JOIN users u ON u.id = ur.user_id WHERE r.code = 'Admin' AND ur.revoked_at IS NULL AND u.deleted_at IS NULL" \
    "user_roles (user_id, role_id, granted_at)"
  ok "copied $(demo_scalar "SELECT count(*) FROM subscription_plans") plans and $(demo_scalar "SELECT count(*) FROM users") admin accounts"
}

delete_demo_customers() {
  say "deleting demo customers"
  demo_psql <<'SQL'
BEGIN;
DELETE FROM invoices WHERE user_id IN (SELECT id FROM users WHERE email LIKE '%@apcs-demo.local');
DELETE FROM subscriptions WHERE user_id IN (SELECT id FROM users WHERE email LIKE '%@apcs-demo.local');
DELETE FROM user_roles WHERE user_id IN (SELECT id FROM users WHERE email LIKE '%@apcs-demo.local');
DELETE FROM users WHERE email LIKE '%@apcs-demo.local';
COMMIT;
SQL
}

seed() {
  if [ "$(demo_scalar "SELECT count(*) FROM users WHERE email LIKE '%@apcs-demo.local'")" -gt 0 ]; then
    ok "demo customers already present (use 'reseed' to regenerate)"
    return
  fi
  say "generating demo customers, subscriptions and invoices"
  demo_psql < "${SEED_SQL}"
  ok "$(demo_scalar "SELECT count(*) FROM users WHERE email LIKE '%@apcs-demo.local'") customers, $(demo_scalar "SELECT count(*) FROM invoices") invoices"
}

print_usage_hint() {
  cat <<EOF

   Point the backend at the demo database (PowerShell, from CAPSTONE_BE):

     \$env:ConnectionStrings__DefaultConnection = "${DEMO_CONNECTION}"
     dotnet run --project API

   The variable wins over .env and only lives in that terminal; close it to go back to the
   team database. Remove everything with: ./scripts/demo/revenue-demo.sh down

EOF
}

case "${1:-up}" in
  up)
    start_container
    load_schema
    copy_reference_data
    seed
    print_usage_hint
    ;;
  reseed)
    start_container
    delete_demo_customers
    seed
    ;;
  down)
    say "removing ${CONTAINER} and its data"
    docker rm -f "${CONTAINER}" >/dev/null 2>&1 || true
    docker volume rm "${VOLUME}" >/dev/null 2>&1 || true
    ok "removed"
    ;;
  *)
    die "unknown command '$1' (use up, reseed or down)"
    ;;
esac
