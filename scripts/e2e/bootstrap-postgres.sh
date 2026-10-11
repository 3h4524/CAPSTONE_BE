#!/usr/bin/env bash
# Dựng PostgreSQL 18 cho test E2E mà KHÔNG cần root.
#
# Máy chỉ có postgresql-client (16). Dump schema của repo chứa
# "SET transaction_timeout" — Postgres 17+ — nên client 16 từ chối, và
# server 16 cũng không replay được. Cách duy nhất không cần sudo là giải
# nén .deb của PGDG vào /tmp rồi chạy initdb từ đó.
#
# /tmp hay bị dọn, nên script này idempotent: chạy lại là khôi phục.

set -euo pipefail

PG_BASE="${PG_BASE:-/tmp/opencode/pg}"
PG_DATA="${PG_DATA:-/tmp/opencode/pgdata18}"
PG_PORT="${PG_PORT:-55432}"
PG_BIN="${PG_BASE}/usr/lib/postgresql/18/bin"
PG_LIB="${PG_BASE}/usr/lib/x86_64-linux-gnu"
DEB_INDEX="https://apt.postgresql.org/pub/repos/apt/pool/main/p/postgresql-18/"
LIBURING_INDEX="http://archive.ubuntu.com/ubuntu/pool/main/libu/liburing/"

log() { printf '[pg-bootstrap] %s\n' "$*"; }
die() { printf '[pg-bootstrap] FATAL: %s\n' "$*" >&2; exit 1; }

if [ -x "${PG_BIN}/pg_ctl" ] && [ -x "${PG_BIN}/initdb" ]; then
  log "binary PostgreSQL 18 đã có ở ${PG_BIN}"
else
  log "tìm bản PostgreSQL 18 mới nhất cho ubuntu 24.04"
  DEB_URL="$(curl -fsSL --max-time 120 "${DEB_INDEX}" \
    | grep -oE 'postgresql-18_1[0-9.]+-[^"]*pgdg24\.04%2B1_amd64\.deb' \
    | sort -V | tail -1)" \
    || die "đọc ${DEB_INDEX} thất bại"
  [ -n "${DEB_URL}" ] || die "không tìm thấy .deb pgdg24.04 trong ${DEB_INDEX}"
  DEB_URL="${DEB_INDEX}${DEB_URL}"
  log "  ${DEB_URL##*/}"

  log "tải PostgreSQL 18 (không cần root)"
  rm -rf "${PG_BASE}" "${PG_BASE}.deb"
  curl -fsSL --max-time 300 -o "${PG_BASE}.deb" "${DEB_URL}" \
    || die "tải ${DEB_URL} thất bại — kiểm tra mạng"
  log "giải nén vào ${PG_BASE}"
  mkdir -p "${PG_BASE}"
  dpkg-deb -x "${PG_BASE}.deb" "${PG_BASE}"
  rm -f "${PG_BASE}.deb"
fi

# PostgreSQL 18 liên kết liburing.so.2, mà Ubuntu 24.04 mặc định không có.
# Không sudo nên giải nén luôn rồi trỏ LD_LIBRARY_PATH vào đó.
if ! LD_LIBRARY_PATH="${PG_LIB}" ldconfig -p 2>/dev/null | grep -q liburing.so.2 \
  && [ ! -e "${PG_LIB}/liburing.so.2" ]; then
  log "máy thiếu liburing.so.2 — tải liburing2 từ Ubuntu archive"
  LIBURING_DEB="$(curl -fsSL --max-time 120 "${LIBURING_INDEX}" \
    | grep -oE 'liburing2_[^"]*amd64\.deb' | sort -V | tail -1)" \
    || die "đọc ${LIBURING_INDEX} thất bại"
  [ -n "${LIBURING_DEB}" ] || die "không tìm thấy liburing2 trong ${LIBURING_INDEX}"
  curl -fsSL --max-time 300 -o "${PG_BASE}.liburing.deb" "${LIBURING_INDEX}${LIBURING_DEB}" \
    || die "tải liburing2 thất bại"
  dpkg-deb -x "${PG_BASE}.liburing.deb" "${PG_BASE}"
  rm -f "${PG_BASE}.liburing.deb"
  log "  liburing2 đã giải nén vào ${PG_LIB}"
fi

[ -x "${PG_BIN}/pg_ctl" ] || die "giải nén xong nhưng không thấy ${PG_BIN}/pg_ctl"

if [ -s "${PG_DATA}/PG_VERSION" ]; then
  log "data directory đã có ở ${PG_DATA}"
else
  log "initdb → ${PG_DATA}"
  rm -rf "${PG_DATA}"
  mkdir -p "${PG_DATA}"
  LD_LIBRARY_PATH="${PG_LIB}" "${PG_BIN}/initdb" \
    -D "${PG_DATA}" -U apcs -E UTF8 --locale=C \
    -A trust >/dev/null \
    || die "initdb thất bại"
fi

if "${PG_BIN}/pg_ctl" -D "${PG_DATA}" status >/dev/null 2>&1; then
  log "postgres đang chạy sẵn"
else
  log "khởi động postgres trên 127.0.0.1:${PG_PORT}"
  # unix_socket_directories phải trỏ vào /tmp: /var/run/postgresql không tồn tại khi
  # không chạy dưới systemd và ta không có quyền root để tạo.
  LD_LIBRARY_PATH="${PG_LIB}" "${PG_BIN}/pg_ctl" -D "${PG_DATA}" \
    -o "-p ${PG_PORT} -c listen_addresses=127.0.0.1 -c unix_socket_directories=/tmp" \
    -l "${PG_DATA}/postgres.log" -w start >/dev/null \
    || die "pg_ctl start thất bại — xem ${PG_DATA}/postgres.log"
fi

log "sẵn sàng. Kiểm tra:"
log "  export PGHOST=127.0.0.1 PGPORT=${PG_PORT} PGUSER=apcs"
log "  ${PG_BIN}/psql -c 'select version()'"