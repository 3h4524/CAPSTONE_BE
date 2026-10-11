#!/usr/bin/env bash
# Dựng database riêng cho integration test.
#
# Tách khỏi apcs_e2e là bắt buộc: apcs_e2e là database mà E2E chạy thật trên đó
# (seed seller, tạo style preset, upload ticket…). Nếu integration test dùng
# chung, dữ liệu của E2E sẽ làm các test đếm/so sánh sai — đã xảy ra: 117
# style preset và 276 user tích tụ làm hỏng 2 test.
#
# APCS_TEST_RESET_DATABASE được đặt khi chạy test để mỗi class wipe sạch bảng.
# Nó chỉ truncate khi tên khớp đúng tên database trong connection string, nên
# không bao giờ xóa nhầm apcs_e2e.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUN_DIR="${REPO_ROOT}/scripts/e2e/.run"
TEST_DB="${TEST_DB:-apcs_test}"
TEST_DB_USER="${TEST_DB_USER:-apcs}"
TEST_DB_PASSWORD="${TEST_DB_PASSWORD:-apcs}"
TEST_PORT="${TEST_PORT:-55432}"
PG_LIB="/tmp/opencode/pg/usr/lib/x86_64-linux-gnu"
# Gói postgresql-18 chỉ chứa binary server; psql client nằm ở gói riêng và máy
# đã có sẵn (client 16 chạy được, chỉ cần bỏ \restrict/\unrestrict trước khi pipe).
PSQL="${PSQL:-psql}"
command -v "${PSQL}" >/dev/null 2>&1 || { printf '[test-db] FATAL: không tìm thấy psql\n' >&2; exit 1; }
BOOTSTRAP_SQL="${REPO_ROOT}/scripts/sql/000-bootstrap-schema.sql"

say() { printf '[test-db] %s\n' "$*"; }

mkdir -p "${RUN_DIR}"

psql_super() {
  LD_LIBRARY_PATH="${PG_LIB}" PGPASSWORD="${TEST_DB_PASSWORD}" \
    "${PSQL}" -h 127.0.0.1 -p "${TEST_PORT}" -U "${TEST_DB_USER}" -d postgres -tAc "$1"
}

if [ "$(psql_super "SELECT 1 FROM pg_database WHERE datname='${TEST_DB}'")" != "1" ]; then
  say "tạo database ${TEST_DB}"
  psql_super "CREATE DATABASE ${TEST_DB}" >/dev/null
else
  say "database ${TEST_DB} đã có"
fi

TABLES="$(LD_LIBRARY_PATH="${PG_LIB}" PGPASSWORD="${TEST_DB_PASSWORD}" \
  "${PSQL}" -h 127.0.0.1 -p "${TEST_PORT}" -U "${TEST_DB_USER}" -d "${TEST_DB}" \
  -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")"

if [ "${TABLES}" != "52" ]; then
  say "nạp schema (${TABLES}/52 bảng hiện tại)"
  # pg_dump sinh \restrict / \unrestrict, chỉ có từ psql 17; client 16 từ chối.
  sed '/^\\restrict /d; /^\\unrestrict /d' "${BOOTSTRAP_SQL}" \
    | LD_LIBRARY_PATH="${PG_LIB}" PGPASSWORD="${TEST_DB_PASSWORD}" \
      "${PSQL}" -h 127.0.0.1 -p "${TEST_PORT}" -U "${TEST_DB_USER}" -d "${TEST_DB}" -q >/dev/null
else
  say "schema đã đủ 52 bảng"
fi

cat <<EOF
export APCS_TEST_CONNECTION_STRING="Host=127.0.0.1;Port=${TEST_PORT};Database=${TEST_DB};Username=${TEST_DB_USER};Password=${TEST_DB_PASSWORD};SSL Mode=Disable"
export APCS_TEST_REDIS_CONNECTION_STRING="127.0.0.1:56379,abortConnect=false"
export APCS_TEST_RESET_DATABASE="${TEST_DB}"
EOF