#!/usr/bin/env bash
# Chuẩn bị database riêng cho integration test.
#
# Tách khỏi apcs_e2e là bắt buộc: apcs_e2e là database mà E2E chạy thật trên đó
# (seed seller, tạo style preset, upload ticket…). Nếu integration test dùng
# chung, dữ liệu của E2E sẽ làm các test đếm/so sánh sai — đã xảy ra: 117 style
# preset và 276 user tích tụ làm hỏng 2 test.
#
# APCS_TEST_RESET_DATABASE được đặt khi chạy test để mỗi class wipe sạch bảng.
# Nó chỉ truncate khi tên khớp đúng tên database trong connection string, nên
# không bao giờ xóa nhầm apcs_e2e.
#
# Script in ra các biến môi trường cần export; run-all.sh eval phần đó.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/scripts/e2e/docker-compose.yml"
BOOTSTRAP_SQL="${REPO_ROOT}/scripts/sql/000-bootstrap-schema.sql"
TEST_DB="${TEST_DB:-apcs_test}"
TEST_DB_USER="${TEST_DB_USER:-apcs}"
TEST_DB_PASSWORD="${TEST_DB_PASSWORD:-apcs}"
TEST_PG_PORT="${TEST_PG_PORT:-55432}"
TEST_REDIS_PORT="${TEST_REDIS_PORT:-56379}"

say() { printf '[test-db] %s\n' "$*"; }

compose() {
  if docker compose version >/dev/null 2>&1; then
    TEST_PG_PORT="${TEST_PG_PORT}" TEST_REDIS_PORT="${TEST_REDIS_PORT}" \
      docker compose -f "${COMPOSE_FILE}" "$@"
  else
    TEST_PG_PORT="${TEST_PG_PORT}" TEST_REDIS_PORT="${TEST_REDIS_PORT}" \
      docker-compose -f "${COMPOSE_FILE}" "$@"
  fi
}
die()  { printf '   \033[1;31mFATAL\033[0m %s\n' "$*" >&2; exit 1; }

sql() {
  compose exec -T postgres env PGPASSWORD="${TEST_DB_PASSWORD}" \
    psql -v ON_ERROR_STOP=1 -qtAX -U "${TEST_DB_USER}" -d "$1" -c "$2" 2>/dev/null
}

if [ "$(sql postgres "SELECT 1 FROM pg_database WHERE datname='${TEST_DB}'")" != "1" ]; then
  say "tạo database ${TEST_DB}"
  sql postgres "CREATE DATABASE ${TEST_DB}" >/dev/null
else
  say "database ${TEST_DB} đã có"
fi

TABLES="$(sql "${TEST_DB}" "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" || echo 0)"

if [ "${TABLES:-0}" -lt 40 ]; then
  say "nạp schema (${TABLES:-0}/52 bảng hiện tại)"
  # pg_dump sinh \restrict / \unrestrict, chỉ có từ psql 17; client cũ từ chối.
  sed '/^\\restrict /d; /^\\unrestrict /d' "${BOOTSTRAP_SQL}" \
    | compose exec -T postgres env PGPASSWORD="${TEST_DB_PASSWORD}" \
        psql -v ON_ERROR_STOP=1 -q -U "${TEST_DB_USER}" -d "${TEST_DB}" >/dev/null
else
  say "schema đã đủ ${TABLES} bảng"
fi

cat <<EOF
export APCS_TEST_CONNECTION_STRING="Host=127.0.0.1;Port=${TEST_PG_PORT};Database=${TEST_DB};Username=${TEST_DB_USER};Password=${TEST_DB_PASSWORD};SSL Mode=Disable"
export APCS_TEST_REDIS_CONNECTION_STRING="127.0.0.1:${TEST_REDIS_PORT},abortConnect=false"
export APCS_TEST_RESET_DATABASE="${TEST_DB}"
EOF