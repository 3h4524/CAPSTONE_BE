#!/usr/bin/env bash
# Starts the E2E backend stack: PostgreSQL, Redis, the SMTP sink, the schema, and the API.
#
# Everything is bound to loopback and every third-party credential is blanked in the process
# environment. DotEnvLoader.Load() skips any key that already exists, and an empty string counts as
# existing, so exporting the blank values below keeps the developer's real .env secrets out of the
# E2E process entirely rather than merely overriding them after the host is built.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
RUN_DIR="${E2E_RUN_DIR:-${SCRIPT_DIR}/.run}"
mkdir -p "${RUN_DIR}"

PG_PORT="${TEST_PG_PORT:-55432}"
PG_HOST="127.0.0.1"
COMPOSE_FILE="${REPO_ROOT}/scripts/e2e/docker-compose.yml"
E2E_DB="${E2E_DB:-apcs_e2e}"
E2E_DB_USER="${E2E_DB_USER:-apcs_e2e}"
E2E_DB_PASSWORD="${E2E_DB_PASSWORD:-apcs_e2e}"
PG_SUPERUSER="${E2E_PG_SUPERUSER:-apcs}"
PG_SUPERPASSWORD="${E2E_PG_SUPERPASSWORD:-apcs}"

REDIS_PORT="${TEST_REDIS_PORT:-56379}"
SMTP_PORT="${E2E_SMTP_PORT:-2525}"
SMTP_FILE="${E2E_SMTP_FILE:-${RUN_DIR}/mail.jsonl}"
API_PORT="${E2E_API_PORT:-5191}"
API_ENVIRONMENT="${E2E_API_ENVIRONMENT:-E2E}"

# Signing key is deterministic and local-only; the API rejects anything shorter than 32 bytes.
JWT_SIGNING_KEY="${E2E_JWT_SIGNING_KEY:-apcs.e2e.signing.key.0123456789abcdef}"

BOOTSTRAP_SQL="${REPO_ROOT}/scripts/sql/000-bootstrap-schema.sql"
API_PROJECT="${REPO_ROOT}/API/API.csproj"
DOTNET=""

API_PID=""
SMTP_PID=""

log()  { printf '\033[1;34m[e2e]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[e2e]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[e2e] FATAL\033[0m %s\n' "$*" >&2; exit 1; }

cleanup_failed_start() {
  local code=$?
  if [ "${code}" -ne 0 ]; then
    warn "startup failed (exit ${code}); stopping whatever this run had already started"
    stop_pid "${RUN_DIR}/api.pid" "API"
    stop_pid "${RUN_DIR}/smtp.pid" "SMTP sink"
    warn "logs: ${RUN_DIR}/api.log ${RUN_DIR}/smtp.log"
  fi
  exit "${code}"
}

# Windows chỉ có lệnh "python"; các bản phân phối khác dùng "python3". Git Bash
# không tự ánh xạ giữa hai tên này, nên phải tự dò trước.
PYTHON=""
# Không ghim đường dẫn của máy nào cả: .NET được cài ở chỗ khác nhau trên mỗi OS.
resolve_dotnet() {
  local candidate
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET="$(command -v dotnet)"
    return 0
  fi
  for candidate in \
    "/c/Program Files/dotnet/dotnet.exe" \
    "/usr/share/dotnet/dotnet" \
    "${HOME}/.dotnet/dotnet"; do
    if [ -x "${candidate}" ]; then
      DOTNET="${candidate}"
      return 0
    fi
  done
  die "không tìm thấy dotnet.
     Cài .NET 8 SDK: https://dotnet.microsoft.com/download
     Nếu đã cài nhưng vẫn báo lỗi, hãy mở lại terminal."
}

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



stop_pid() {
  local pid_file="$1"
  local label="$2"

  [ -s "${pid_file}" ] || return 0

  local pid
  pid="$(cat "${pid_file}")"
  if kill -0 "${pid}" 2>/dev/null; then
    kill "${pid}" 2>/dev/null || true
    for _ in $(seq 1 50); do
      kill -0 "${pid}" 2>/dev/null || break
      sleep 0.2
    done
    kill -9 "${pid}" 2>/dev/null || true
    log "${label} stopped (pid ${pid})"
  fi

  rm -f "${pid_file}"
}

require() {
  command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

# Mọi thao tác SQL đi qua container thay vì psql trên host. psql là binary của
# Postgres, không có sẵn trên Windows; docker compose thì có ở cả ba OS. Nhờ vậy
# script này không cần cài gì ngoài Docker.
psql_exec() {
  local user="$1" db="$2" statement="$3"
  TEST_PG_PORT="${PG_PORT}" TEST_REDIS_PORT="${REDIS_PORT}" \
    e2e_compose exec -T postgres \
      env PGPASSWORD="${E2E_DB_PASSWORD}" \
      psql -v ON_ERROR_STOP=1 -qtAX -U "${user}" -d "${db}" -c "${statement}"
}

pg_super_psql() {
  psql_exec "${E2E_SUPERUSER:-apcs}" postgres "$1"
}

pg_query() {
  psql_exec "${E2E_DB_USER}" "$1" "$2"
}

port_open() {
  # An explicit connect timeout is required: on this host a closed loopback port drops the SYN
  # instead of refusing it, so a bare /dev/tcp connect blocks for minutes.
  ${PYTHON} - "${1:-}" "${2:-}" <<'PY'
import socket
import sys

host, port = sys.argv[1], int(sys.argv[2])
try:
    with socket.create_connection((host, port), timeout=1):
        pass
except OSError:
    sys.exit(1)
PY
}

# Postgres và Redis do bootstrap-postgres.sh quản lý qua Docker. Script này chỉ
# chờ chúng sẵn sàng — không tự khởi động, để không có hai nơi cùng sở hữu vòng
# đời của container.
e2e_compose() {
  if docker compose version >/dev/null 2>&1; then
    TEST_PG_PORT="${PG_PORT}" TEST_REDIS_PORT="${REDIS_PORT}" \
      docker compose -f "${COMPOSE_FILE}" "$@"
  else
    TEST_PG_PORT="${PG_PORT}" TEST_REDIS_PORT="${REDIS_PORT}" \
      docker-compose -f "${COMPOSE_FILE}" "$@"
  fi
}

require_docker() {
  command -v docker >/dev/null 2>&1 \
    || die "docker chưa được cài. Xem bootstrap-postgres.sh để biết cách cài cho OS của bạn."
  docker info >/dev/null 2>&1 \
    || die "Docker daemon chưa chạy. Mở Docker Desktop (hoặc: sudo systemctl start docker)."
}

ensure_postgres() {
  local attempt
  for attempt in $(seq 1 60); do
    if e2e_compose exec -T postgres pg_isready -U "${E2E_DB_USER}" -d postgres >/dev/null 2>&1; then
      log "postgres container sẵn sàng trên ${PG_HOST}:${PG_PORT}"
      return 0
    fi
    sleep 1
  done
  die "postgres container không sẵn sàng sau 60s. Chạy: ${REPO_ROOT}/scripts/e2e/bootstrap-postgres.sh"
}

ensure_redis() {
  local attempt
  for attempt in $(seq 1 30); do
    if e2e_compose exec -T redis redis-cli ping 2>/dev/null | grep -q PONG; then
      log "redis container sẵn sàng trên 127.0.0.1:${REDIS_PORT}"
      return 0
    fi
    sleep 1
  done
  die "redis container không trả lời PING sau 30s"
}

ensure_database() {
  local exists
  exists="$(pg_super_psql "SELECT 1 FROM pg_database WHERE datname = '${E2E_DB}'" | tr -d '[:space:]')"

  if [ "${exists}" != "1" ]; then
    log "creating database ${E2E_DB}"
    pg_super_psql "CREATE DATABASE ${E2E_DB} OWNER ${E2E_DB_USER}" \
      || die "could not create the database ${E2E_DB}"
  else
    log "database ${E2E_DB} already exists"
  fi
}

ensure_role() {
  local exists
  exists="$(pg_super_psql "SELECT 1 FROM pg_roles WHERE rolname = '${E2E_DB_USER}'" | tr -d '[:space:]')"

  if [ "${exists}" != "1" ]; then
    log "creating role ${E2E_DB_USER}"
    pg_super_psql "CREATE ROLE ${E2E_DB_USER} LOGIN CREATEDB PASSWORD '${E2E_DB_PASSWORD}'" \
      || die "could not create the role ${E2E_DB_USER}"
  else
    pg_super_psql "ALTER ROLE ${E2E_DB_USER} LOGIN CREATEDB PASSWORD '${E2E_DB_PASSWORD}'" \
      || die "could not update the role ${E2E_DB_USER}"
  fi
}

ensure_schema() {
  [ -f "${BOOTSTRAP_SQL}" ] || die "bootstrap schema is missing: ${BOOTSTRAP_SQL}"

  local tables
  tables="$(pg_query "${E2E_DB}" \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'" | tr -d '[:space:]')"

  if [ "${tables:-0}" -ge 40 ]; then
    log "schema already loaded (${tables} tables in public); skipping bootstrap dump"
    return 0
  fi

  if [ "${tables:-0}" -gt 0 ]; then
    warn "public schema has only ${tables} tables, which is a partial load; re-applying the bootstrap dump"
  fi

  # pg_dump 18 emits \restrict / \unrestrict, which older psql clients reject, and the local client
  # is not always the same major version as the server.
  log "loading ${BOOTSTRAP_SQL##*/} into ${E2E_DB}"
  sed -e '/^\\restrict /d' -e '/^\\unrestrict /d' "${BOOTSTRAP_SQL}" \
    | e2e_compose exec -T postgres \
        env PGPASSWORD="${E2E_DB_PASSWORD}" \
        psql -v ON_ERROR_STOP=1 -q -U "${E2E_DB_USER}" -d "${E2E_DB}" >"${RUN_DIR}/schema.log" 2>&1 \
    || { tail -20 "${RUN_DIR}/schema.log" >&2; die "bootstrap schema load failed; see ${RUN_DIR}/schema.log"; }

  tables="$(pg_query "${E2E_DB}" \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'" | tr -d '[:space:]')"
  [ "${tables:-0}" -ge 40 ] || die "bootstrap load finished but only ${tables:-0} tables exist"

  log "schema loaded (${tables} tables in public)"
}

start_smtp_sink() {
  if port_open 127.0.0.1 "${SMTP_PORT}"; then
    log "smtp sink already listening on 127.0.0.1:${SMTP_PORT}"
    return 0
  fi

  log "starting smtp sink on 127.0.0.1:${SMTP_PORT} -> ${SMTP_FILE}"
  ${PYTHON} "${SCRIPT_DIR}/smtp-sink.py" \
    --host 127.0.0.1 --port "${SMTP_PORT}" --out "${SMTP_FILE}" \
    --pid-file "${RUN_DIR}/smtp.pid" --reset \
    >"${RUN_DIR}/smtp.log" 2>&1 &
  SMTP_PID=$!
  echo "${SMTP_PID}" >"${RUN_DIR}/smtp.pid"

  local attempt
  for attempt in $(seq 1 40); do
    port_open 127.0.0.1 "${SMTP_PORT}" && { log "smtp sink ready (pid ${SMTP_PID})"; return 0; }
    kill -0 "${SMTP_PID}" 2>/dev/null || die "smtp sink exited immediately; see ${RUN_DIR}/smtp.log"
    sleep 0.25
  done

  die "smtp sink did not bind port ${SMTP_PORT}; see ${RUN_DIR}/smtp.log"
}

start_api() {
  if [ -s "${RUN_DIR}/api.pid" ] && kill -0 "$(cat "${RUN_DIR}/api.pid")" 2>/dev/null; then
    die "an API started by an earlier run is still alive (pid $(cat "${RUN_DIR}/api.pid")).
    Run scripts/e2e/stop-backend.sh first."
  fi

  log "building API (restore happens here, once, so the running API never touches the network)"
  "${DOTNET}" build "${API_PROJECT}" -v quiet -p:NuGetAudit=false \
    >"${RUN_DIR}/build.log" 2>&1 \
    || { tail -30 "${RUN_DIR}/build.log" >&2; die "dotnet build failed; see ${RUN_DIR}/build.log"; }

  log "starting API on http://localhost:${API_PORT} (environment ${API_ENVIRONMENT})"
  nohup "${DOTNET}" run --project "${API_PROJECT}" --no-build --no-launch-profile \
    >"${RUN_DIR}/api.log" 2>&1 &
  API_PID=$!
  echo "${API_PID}" >"${RUN_DIR}/api.pid"

  log "waiting for http://localhost:${API_PORT}/health"
  local attempt status
  for attempt in $(seq 1 180); do
    if ! kill -0 "${API_PID}" 2>/dev/null; then
      tail -40 "${RUN_DIR}/api.log" >&2
      die "the API process exited before answering /health; see ${RUN_DIR}/api.log"
    fi

    status="$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 \
      "http://localhost:${API_PORT}/health" || true)"

    if [ "${status}" = "200" ]; then
      log "API healthy (pid ${API_PID})"
      return 0
    fi

    sleep 1
  done

  tail -40 "${RUN_DIR}/api.log" >&2
  die "the API did not answer 200 on /health within 180s; see ${RUN_DIR}/api.log"
}

main() {
  resolve_python
  resolve_dotnet
  trap cleanup_failed_start EXIT

  require curl
  require python3
  require_docker

  [ -f "${API_PROJECT}" ] || die "API project not found: ${API_PROJECT}"
  [ -n "${DOTNET}" ] || die "dotnet chưa được dò thấy"

  ensure_postgres
  ensure_redis
  ensure_role
  ensure_database
  ensure_schema
  start_smtp_sink

  # ── process environment: everything the API reads after DotEnvLoader ──────────────
  # `__` is the ASP.NET Core separator for `:` in configuration keys.
  export ConnectionStrings__DefaultConnection="Host=${PG_HOST};Port=${PG_PORT};Database=${E2E_DB};Username=${E2E_DB_USER};Password=${E2E_DB_PASSWORD};SSL Mode=Disable;Include Error Detail=false"
  export Redis__ConnectionString="127.0.0.1:${REDIS_PORT},abortConnect=false"
  export Redis__InstanceName="APCS:e2e:"
  export Redis__DefaultExpirationMinutes="10"

  export Jwt__Issuer="APCS.E2E"
  export Jwt__Audience="APCS.Web"
  export Jwt__SigningKey="${JWT_SIGNING_KEY}"
  export Jwt__AccessTokenMinutes="30"
  export Jwt__RefreshTokenDays="7"
  export Jwt__EmailVerificationHours="24"
  export Jwt__PasswordResetMinutes="30"

  export App__BaseUrl="${E2E_APP_BASE_URL:-http://localhost:4010}"
  export App__VerifyEmailPath="/verify-email"
  export App__ResetPasswordPath="/reset-password"

  export Cors__AllowedOrigins="${E2E_CORS_ORIGINS:-http://localhost:4010;http://localhost:4020}"

  # Points at the loopback sink. Username and Password are required by SmtpOptions.IsConfigured,
  # so without them the service would take the "log only" branch and the token would be lost.
  export Smtp__Host="127.0.0.1"
  export Smtp__Port="${SMTP_PORT}"
  export Smtp__UseStartTls="false"
  export Smtp__Username="e2e"
  export Smtp__Password="e2e"
  export Smtp__FromAddress="e2e@test.local"
  export Smtp__FromName="APCS E2E"

  # Blanked on purpose: an empty value blocks the .env secret, and every adapter here treats a
  # missing value as "not configured" rather than failing to start.
  export Authentication__Google__ClientId=""
  export Cloudinary__CloudName=""
  export Cloudinary__ApiKey=""
  export Cloudinary__ApiSecret=""
  export Cloudinary__Folder=""
  # Numeric options cannot be bound from an empty string, so they get a neutral value.
  export Cloudinary__SignedUrlTtlMinutes="10"
  export PayOS__ClientId=""
  export PayOS__ApiKey=""
  export PayOS__ChecksumKey=""
  export PayOS__UsdToVndRate="25000"
  export PayOS__TestAmountVnd="0"

  # Not Development: that profile exposes /api/dev/* endpoints which call PayOS for real.
  export ASPNETCORE_ENVIRONMENT="${API_ENVIRONMENT}"
  export ASPNETCORE_URLS="http://localhost:${API_PORT}"
  export DOTNET_ENVIRONMENT="${API_ENVIRONMENT}"
  # Telemetry would otherwise open an outbound connection from the API process.
  export DOTNET_CLI_TELEMETRY_OPTOUT="1"
  export DOTNET_NOLOGO="1"
  export Logging__LogLevel__Default="Information"
  export Logging__LogLevel__Microsoft_AspNetCore="Warning"

  # PayOsGatewayClient is the one adapter with no "not configured" guard, so a test that reaches a
  # checkout route would still try the real API. .NET reads the proxy environment for every
  # HttpClient, so pointing it at a closed loopback port makes outbound HTTP fail fast while
  # loopback traffic to PostgreSQL, Redis and the SMTP sink stays direct.
  if [ "${E2E_PROXY_GUARD:-on}" = "on" ]; then
    export ALL_PROXY="${E2E_PROXY_TARGET:-http://127.0.0.1:1}"
    export all_proxy="${ALL_PROXY}"
    export NO_PROXY="localhost,127.0.0.1,::1"
    export no_proxy="${NO_PROXY}"
    log "egress guard on: outbound HttpClient traffic is proxied to ${ALL_PROXY} and fails"
  else
    warn "egress guard is OFF (E2E_PROXY_GUARD=off); outbound HTTP can leave this machine"
  fi

  start_api

  cat >"${RUN_DIR}/env.sh" <<EOF
# Sourced by seed-seller.sh and stop-backend.sh. Values are test-only, never production secrets.
export E2E_BASE_URL="http://localhost:${API_PORT}"
export E2E_API_PORT="${API_PORT}"
export E2E_SMTP_PORT="${SMTP_PORT}"
export E2E_SMTP_FILE="${SMTP_FILE}"
export E2E_RUN_DIR="${RUN_DIR}"
export E2E_PG_PORT="${PG_PORT}"
export E2E_DB="${E2E_DB}"
export E2E_DB_USER="${E2E_DB_USER}"
export E2E_DB_PASSWORD="${E2E_DB_PASSWORD}"
export E2E_APP_BASE_URL="${E2E_APP_BASE_URL:-http://localhost:4010}"
export E2E_CORS_ORIGINS="${E2E_CORS_ORIGINS:-http://localhost:4010;http://localhost:4020}"
EOF

  trap - EXIT

  log "─────────────────────────────────────────────"
  log "E2E backend is up"
  log "  base url     http://localhost:${API_PORT}   (pid ${API_PID})"
  log "  health       http://localhost:${API_PORT}/health"
  log "  postgres     ${PG_HOST}:${PG_PORT}/${E2E_DB}"
  log "  redis        127.0.0.1:${REDIS_PORT}"
  log "  smtp sink    127.0.0.1:${SMTP_PORT} -> ${SMTP_FILE}"
  log "  run dir      ${RUN_DIR}"
  log "  blocked third parties: Cloudinary, PayOS, Google Sign-In, real SMTP"
  log "  next         scripts/e2e/seed-seller.sh"
  log "─────────────────────────────────────────────"
}

main "$@"