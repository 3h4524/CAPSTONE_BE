#!/usr/bin/env bash
# Stops everything scripts/e2e/start-backend.sh started, and leaves the database in place.
#
# PostgreSQL and Redis are only touched when this run is the one that brought them up, or when
# --all is passed: the shared E2E endpoints are reused by the integration test suite, so stopping
# them by default would break a parallel run.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_DIR="${E2E_RUN_DIR:-${SCRIPT_DIR}/.run}"

PG_PORT="${E2E_PG_PORT:-55432}"
PG_DATA_DIR="${E2E_PG_DATA_DIR:-/tmp/opencode/pgdata18}"
PG_ROOT="${E2E_PG_ROOT:-/tmp/opencode/pg18}"
PG_BIN="${PG_ROOT}/root2404/usr/lib/postgresql/18/bin"
PG_LIB="${PG_ROOT}/root2404/usr/lib/x86_64-linux-gnu"
REDIS_PORT="${E2E_REDIS_PORT:-56379}"

STOP_INFRASTRUCTURE="no"
[ "${1:-}" = "--all" ] && STOP_INFRASTRUCTURE="yes"

log()  { printf '\033[1;34m[e2e]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[e2e]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[e2e] FATAL\033[0m %s\n' "$*" >&2; exit 1; }

collect_process_tree() {
  # `dotnet run` supervises msbuild and Kestrel as descendants, so stopping the recorded pid alone
  # would leave the API holding its port. Depth-first order lets the caller kill children first.
  local pid="$1" child
  echo "${pid}"
  while read -r child; do
    [ -n "${child}" ] && collect_process_tree "${child}"
  done < <(pgrep -P "${pid}" 2>/dev/null || true)
}

stop_pid_file() {
  local pid_file="$1" label="$2"

  if [ ! -s "${pid_file}" ]; then
    log "${label}: no pid file, nothing to stop"
    return 0
  fi

  local root
  root="$(cat "${pid_file}")"

  if ! kill -0 "${root}" 2>/dev/null; then
    rm -f "${pid_file}"
    log "${label}: pid ${root} was already gone"
    return 0
  fi

  local tree
  tree="$(collect_process_tree "${root}" | tac)"

  local signal waited
  for signal in TERM KILL; do
    local pid
    for pid in ${tree}; do
      kill "-${signal}" "${pid}" 2>/dev/null || true
    done

    waited=0
    while [ "${waited}" -lt 60 ]; do
      local alive=0 pid
      for pid in ${tree}; do
        kill -0 "${pid}" 2>/dev/null && alive=1
      done
      [ "${alive}" -eq 0 ] && break
      sleep 0.5
      waited=$((waited + 1))
    done
  done

  rm -f "${pid_file}"
  log "${label} stopped (${tree//$'\n'/ })"
}

port_open() {
  # An explicit connect timeout is required: on this host a closed loopback port drops the SYN
  # instead of refusing it, so a bare /dev/tcp connect blocks for minutes.
  python3 - "${1:-}" <<'PY'
import socket
import sys

try:
    with socket.create_connection(("127.0.0.1", int(sys.argv[1])), timeout=1):
        pass
except OSError:
    sys.exit(1)
PY
}

stop_redis() {
  if ! redis-cli -p "${REDIS_PORT}" ping 2>/dev/null | grep -q PONG; then
    log "redis: not listening on ${REDIS_PORT}, nothing to stop"
    return 0
  fi

  redis-cli -p "${REDIS_PORT}" shutdown nosave >/dev/null 2>&1 \
    || { warn "redis did not shut down cleanly"; return 0; }

  log "redis stopped on ${REDIS_PORT}"
}

stop_postgres() {
  if ! port_open "${PG_PORT}"; then
    log "postgres: not listening on ${PG_PORT}, nothing to stop"
    return 0
  fi

  if [ ! -x "${PG_BIN}/pg_ctl" ]; then
    warn "postgres: pg_ctl missing under ${PG_BIN}, leaving the server running"
    return 0
  fi

  LD_LIBRARY_PATH="${PG_LIB}" "${PG_BIN}/pg_ctl" -D "${PG_DATA_DIR}" -m fast stop >/dev/null 2>&1 \
    || { warn "postgres did not shut down cleanly; see ${RUN_DIR}/postgres.log"; return 0; }

  log "postgres stopped on ${PG_PORT}"
}

main() {
  [ -d "${RUN_DIR}" ] || { log "no run directory at ${RUN_DIR}, nothing to stop"; exit 0; }

  stop_pid_file "${RUN_DIR}/api.pid" "API"
  stop_pid_file "${RUN_DIR}/smtp.pid" "SMTP sink"

  # Catches an API whose supervisor died before the pid file was cleaned up.
  pkill -f "${SCRIPT_DIR%/scripts/e2e}/API/bin/.*/APCS.Api" 2>/dev/null \
    && warn "killed a leftover APCS.Api process from this worktree" || true

  if port_open 5191; then
    warn "something is still listening on 5191; check ${RUN_DIR}/api.log"
  fi

  if [ "${STOP_INFRASTRUCTURE}" = "yes" ]; then
    stop_redis
    stop_postgres
  else
    log "keeping PostgreSQL (${PG_PORT}) and Redis (${REDIS_PORT}) running; pass --all to stop them too"
  fi

  log "database left untouched; re-run start-backend.sh to bring the stack back"
}

main "$@"