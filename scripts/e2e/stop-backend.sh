#!/usr/bin/env bash
# Stops everything scripts/e2e/start-backend.sh started, and leaves the database in place.
#
# PostgreSQL and Redis are only touched when this run is the one that brought them up, or when
# --all is passed: the shared E2E endpoints are reused by the integration test suite, so stopping
# them by default would break a parallel run.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_DIR="${E2E_RUN_DIR:-${SCRIPT_DIR}/.run}"

PG_PORT="${TEST_PG_PORT:-55432}"
COMPOSE_FILE="${COMPOSE_FILE:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/docker-compose.yml}"
REDIS_PORT="${TEST_REDIS_PORT:-56379}"

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

# Windows chỉ có lệnh "python"; các bản phân phối khác dùng "python3". Git Bash
# không tự ánh xạ giữa hai tên này, nên phải tự dò trước.
PYTHON=""
resolve_python() {
  # `command -v` là chưa đủ: Windows có sẵn shim trỏ tới Microsoft Store, tên
  # lệnh là "python" nhưng chạy ra thông báo "Python was not found". Phải thực
  # sự chạy thật rồi mới tin.
  local candidate
  for candidate in python3 python; do
    if command -v "${candidate}" >/dev/null 2>&1 && "${candidate}" --version >/dev/null 2>&1; then
      PYTHON="${candidate}"
      return 0
    fi
  done
  for candidate in \
    "/c/Python312/python.exe" "/c/Python313/python.exe" "/c/Python311/python.exe" \
    "/c/Program Files/Python312/python.exe" "/c/Program Files/Python313/python.exe"; do
    if [ -x "${candidate}" ] && "${candidate}" --version >/dev/null 2>&1; then
      PYTHON="${candidate}"
      return 0
    fi
  done
  die "không tìm thấy Python 3.
     Windows: winget install Python.Python.3.12
     macOS  : brew install python
     Ubuntu : sudo apt install python3

     Nếu đã cài nhưng vẫn báo lỗi, hãy mở lại terminal — PATH chỉ cập nhật ở terminal mới."
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
  ${PYTHON} - "${1:-}" <<'PY'
import socket
import sys

try:
    with socket.create_connection(("127.0.0.1", int(sys.argv[1])), timeout=1):
        pass
except OSError:
    sys.exit(1)
PY
}

compose() {
  if docker compose version >/dev/null 2>&1; then
    TEST_PG_PORT="${PG_PORT}" TEST_REDIS_PORT="${REDIS_PORT}" \
      docker compose -f "${COMPOSE_FILE}" "$@"
  else
    TEST_PG_PORT="${PG_PORT}" TEST_REDIS_PORT="${REDIS_PORT}" \
      docker-compose -f "${COMPOSE_FILE}" "$@"
  fi
}

# Postgres và Redis do Docker quản lý: dừng cả cặp container thay vì tự quản lý
# tiến trình. Không có docker thì bỏ qua — không phải lỗi.
stop_services() {
  command -v docker >/dev/null 2>&1 || { log "docker not available, leaving containers alone"; return 0; }
  docker info >/dev/null 2>&1 || { log "docker daemon not running, leaving containers alone"; return 0; }

  if [ "${1:-}" = "--all" ]; then
    compose down -v >/dev/null 2>&1 && log "containers removed (data discarded)" \
      || warn "docker compose down failed"
  else
    compose stop >/dev/null 2>&1 && log "containers stopped (data kept)" \
      || warn "docker compose stop failed"
  fi
}

main() {
  resolve_python
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
    stop_services "$@"
  else
    log "keeping the Postgres and Redis containers running; pass --all to remove them"
  fi

  log "database left untouched; re-run start-backend.sh to bring the stack back"
}

main "$@"