#!/usr/bin/env bash
# Bật PostgreSQL 18 + Redis cho test, qua Docker.
#
# Đây là điểm khác biệt DUY NHẤT giữa các hệ điều hành. Mọi script còn lại chỉ
# dùng curl, python3, dotnet và pnpm — tức là chạy giống nhau trên Windows,
# macOS và Linux. Docker Desktop có sẵn cho cả ba nên không cần ai phải cài
# Postgres thủ công, và /tmp bị dọn cũng không ảnh hưởng.
#
# Cũ trước đây script này tải .deb của PostgreSQL rồi chạy initdb — chỉ chạy
# được trên Linux.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/scripts/e2e/docker-compose.yml"
TEST_PG_PORT="${TEST_PG_PORT:-55432}"
TEST_REDIS_PORT="${TEST_REDIS_PORT:-56379}"

say()  { printf '\033[1;34m[e2e]\033[0m %s\n' "$*"; }
ok()   { printf '   \033[32m%s\033[0m\n' "$*"; }
bad()  { printf '   \033[31m%s\033[0m\n' "$*"; }

compose() {
  if docker compose version >/dev/null 2>&1; then
    TEST_PG_PORT="${TEST_PG_PORT}" TEST_REDIS_PORT="${TEST_REDIS_PORT}" \
      docker compose -f "${COMPOSE_FILE}" "$@"
  else
    TEST_PG_PORT="${TEST_PG_PORT}" TEST_REDIS_PORT="${TEST_REDIS_PORT}" \
      docker-compose -f "${COMPOSE_FILE}" "$@"
  fi
}

# Windows chỉ có lệnh "python"; các bản phân phối khác dùng "python3". Git Bash
# không tự ánh xạ giữa hai tên này, nên phải tự dò trước.
PYTHON=""
resolve_python() {
  PYTHON="$(command -v python3 || command -v python || true)"
  [ -n "${PYTHON}" ] || die "cần Python 3 (cài từ python.org hoặc: winget install Python.Python.3.12)"
}



require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    bad "không tìm thấy lệnh 'docker'"
    printf '\n'
    printf '   Cài Docker:\n'
    printf '     Windows / macOS : https://docs.docker.com/desktop/\n'
    printf '     Linux           : https://docs.docker.com/engine/install/\n'
    printf '\n   Trên Windows, sau khi cài còn phải bật WSL integration cho distro của bạn:\n'
    printf '     Docker Desktop > Settings > Resources > WSL Integration > tick distro > Apply & Restart\n'
    printf '\n   Sau đó chạy lại: %s\n' "$0"
    exit 1
  fi

  if ! docker info >/dev/null 2>&1; then
    bad "Docker có lệnh nhưng daemon chưa chạy"
    printf '\n'
    printf '   Windows / macOS : mở app Docker Desktop và đợi tới khi Engine chạy\n'
    printf '   Linux           : sudo systemctl start docker\n'
    printf '\n   Nếu bạn ở trong WSL mà vẫn lỗi, xem phần WSL integration ở trên.\n'
    exit 1
  fi
}

resolve_python
require_docker
ok "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?') sẵn sàng"

# Docker không báo lỗi khi không bind được port đã bị chiếm — container vẫn
# "healthy" nhưng `docker port` rỗng và mọi kết nối từ host đều rơi vào dịch vụ
# khác đang giữ port đó. Vì vậy phải kiểm tra trước khi khởi động.
port_free() {
  local port="$1"
  ${PYTHON} - "$port" <<'PY'
import socket, sys
port = int(sys.argv[1])
probe = socket.socket()
probe.settimeout(1)
sys.exit(1 if probe.connect_ex(("127.0.0.1", port)) == 0 else 0)
PY
}

for pair in "${TEST_PG_PORT}:PostgreSQL:apcs-e2e-postgres" "${TEST_REDIS_PORT}:Redis:apcs-e2e-redis"; do
  port="${pair%%:*}"
  rest="${pair#*:}"
  label="${rest%%:*}"
  container="${rest##*:}"

  # Port đang bị container của chính lần chạy này giữ thì không phải xung đột.
  if [ "$(docker ps --filter "name=^/${container}$" --filter status=running -q | wc -l | tr -d ' ')" != "0" ]; then
    continue
  fi

  if ! port_free "${port}"; then
    bad "port ${port} (${label}) đã bị dịch vụ khác chiếm."
    printf '\n'
    printf '   Docker sẽ không publish được port này và mọi kết nối sẽ rơi nhầm vào\n'
    printf '   dịch vụ đang chiếm. Chọn port khác, ví dụ:\n'
    printf '\n     TEST_PG_PORT=55432 TEST_REDIS_PORT=56379 %s\n' "$0"
    printf '\n   Hoặc dừng dịch vụ đang dùng port %s.\n' "${port}"
    printf '   Xem ai đang giữ: ss -ltnp (Linux/macOS) hoặc netstat -ano (Windows)\n'
    exit 1
  fi
done

say "khởi động PostgreSQL 18 + Redis"
compose up -d --remove-orphans >/dev/null 2>&1 \
  || { bad "docker compose up thất bại"; exit 1; }

say "chờ PostgreSQL nhận kết nối"
for _ in $(seq 1 60); do
  if compose exec -T postgres pg_isready -U "${TEST_DB_USER:-apcs}" -d postgres >/dev/null 2>&1; then
    ok "postgres 127.0.0.1:${TEST_PG_PORT}"
    break
  fi
  sleep 1
done

say "chờ Redis nhận kết nối"
for _ in $(seq 1 30); do
  if compose exec -T redis redis-cli ping 2>/dev/null | grep -q PONG; then
    ok "redis 127.0.0.1:${TEST_REDIS_PORT}"
    break
  fi
  sleep 1
done

compose ps --format 'table {{.Name}}\t{{.Status}}' 2>/dev/null | sed 's/^/   /' || true

# Container healthy chưa chắc host đã publish được port — xác nhận bằng cách thử
# kết nối thật từ ngoài container.
if ! port_free "${TEST_PG_PORT}"; then
  ok "postgres container nhận kết nối trên 127.0.0.1:${TEST_PG_PORT}"
else
  bad "postgres container healthy nhưng port ${TEST_PG_PORT} không nhận kết nối từ host."
  bad "Docker Desktop có thể chưa publish được port. Kiểm tra: docker port apcs-e2e-postgres"
  exit 1
fi

printf '\n'
ok "hạ tầng sẵn sàng"