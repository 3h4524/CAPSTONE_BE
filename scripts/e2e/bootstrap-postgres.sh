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
die()  { printf '   \033[1;31mFATAL\033[0m %s\n' "$*" >&2; exit 1; }
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
  # `command -v` là chưa đủ: Windows có sẵn một shim trỏ tới Microsoft Store, tên
  # lệnh là "python" nhưng chạy ra thông báo "Python was not found". Phải thực
  # sự chạy thật rồi mới tin.
  local candidate
  for candidate in python3 python; do
    if command -v "${candidate}" >/dev/null 2>&1 && "${candidate}" --version >/dev/null 2>&1; then
      PYTHON="${candidate}"
      return 0
    fi
  done

  # Git Bash đôi khi không thấy Python đã cài dù PATH đã có; thử lần nữa qua
  # đường dẫn cài mặc định của Windows.
  for candidate in \
    "/c/Python312/python.exe" \
    "/c/Python313/python.exe" \
    "/c/Python311/python.exe" \
    "/c/Program Files/Python312/python.exe" \
    "/c/Program Files/Python313/python.exe"; do
    if [ -x "${candidate}" ] && "${candidate}" --version >/dev/null 2>&1; then
      PYTHON="${candidate}"
      return 0
    fi
  done

  die "không tìm thấy Python 3.
     Windows: winget install Python.Python.3.12
     macOS  : brew install python
     Ubuntu : sudo apt install python3

     Nếu đã cài nhưng vẫn báo lỗi, hãy mở lại terminal — PATH chỉ cập nhật ở
     terminal mới."
}

# Chẩn đoán khi Docker hoặc Python thiếu. Trên Windows cả hai thường cài xong mà
# chưa mở lại terminal, nên PATH của Git Bash vẫn cũ.
diagnose() {
  printf '\n   Chẩn đoán:\n'
  printf '     docker   : %s\n' "$(command -v docker >/dev/null 2>&1 && echo 'có' || echo 'KHÔNG có trong PATH')"
  if [ -n "${PYTHON}" ]; then
    printf '     python   : %s (%s)\n' "${PYTHON}" "$("${PYTHON}" --version 2>&1)"
  else
    printf '     python   : KHÔNG chạy được\n'
  fi
  printf '     hệ điều hành: %s\n' "$(uname -s 2>/dev/null || echo '?')"
  printf '\n'
  if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    printf '   Docker Desktop có đang chạy không? Mở app và đợi tới khi Engine xanh.\n'
    printf '   Nếu vừa cài, hãy mở lại Git Bash — PATH chỉ cập nhật ở terminal mới.\n'
  fi
  printf '\n'
}



require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    bad "không tìm thấy lệnh 'docker'"
    printf '\n'
    printf '   Cài Docker:\n'
    printf '     Windows / macOS : https://docs.docker.com/desktop/\n'
    printf '     Linux           : https://docs.docker.com/engine/install/\n'
    printf '\n   Chạy trong Git Bash trên Windows, không cần WSL.\n'
    printf '   Sau khi cài, mở lại Git Bash để PATH được cập nhật.\n'
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

resolve_python || { diagnose; exit 1; }
require_docker || { diagnose; exit 1; }
ok "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?') sẵn sàng"

# Chặn port trước khi khởi động là sai: một container đang `docker compose down`
# vẫn giữ port thêm vài trăm ms, và socket TIME_WAIT của lần chạy trước làm
# check sai. Cách đúng là khởi động trước rồi mới kiểm tra bằng kết nối thật.
# Lưu ý: chỉ container cần dừng mới đây là container của chính lần chạy này,
# không đụng tới dịch vụ nào khác — script không bao giờ tự kill process lạ.

# connect_ex chỉ trả 0 khi thật sự có ai listen. Socket TIME_WAIT của lần chạy
# trước không làm sai kết quả này, nên đây là cách kiểm tra đáng tin hơn việc
# đọc netstat.
host_accepts_connections() {
  local port="$1"
  ${PYTHON} - "${port}" <<'PYEOF_SOCKET'
import socket, sys
probe = socket.socket()
probe.settimeout(2)
sys.exit(0 if probe.connect_ex(("127.0.0.1", int(sys.argv[1]))) == 0 else 1)
PYEOF_SOCKET
}

say "khởi động PostgreSQL 18 + Redis"
compose up -d --remove-orphans >/dev/null 2>&1 \
  || { bad "docker compose up thất bại"; exit 1; }

# Đợi Postgres nhận kết nối.
for _ in $(seq 1 60); do
  if compose exec -T postgres pg_isready -U "${TEST_DB_USER:-apcs}" -d postgres >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

# Đợi Redis trả lời.
for _ in $(seq 1 30); do
  if compose exec -T redis redis-cli ping 2>/dev/null | grep -q PONG; then
    break
  fi
  sleep 1
done

# Container healthy chưa chắc host đã publish được port. Nếu port đã bị dịch vụ
# khác giữ, Docker không báo lỗi mà bỏ qua, và mọi kết nối rơi nhầm vào đó —
# nên phải thử nối thật từ host sau khi container đã healthy.
for pair in "${TEST_PG_PORT}:PostgreSQL" "${TEST_REDIS_PORT}:Redis"; do
  port="${pair%%:*}"
  label="${pair##*:}"
  if host_accepts_connections "${port}"; then
    ok "${label} nhận kết nối trên 127.0.0.1:${port}"
  else
    bad "port ${port} (${label}) đang bị dịch vụ khác chiếm — container đã lên"
    bad "nhưng mọi kết nối sẽ rơi vào dịch vụ đó, không phải vào container."
    printf '\n'
    printf '   Chọn port khác:\n'
    printf '     TEST_PG_PORT=%s TEST_REDIS_PORT=%s %s\n' "$((TEST_PG_PORT + 8))" "$((TEST_REDIS_PORT + 8))" "$0"
    printf '\n   Hoặc dừng dịch vụ đang dùng port %s rồi chạy lại.\n' "${port}"
    printf '   Windows : netstat -ano | findstr LISTENING | findstr :%s\n' "${port}"
    printf '   Linux   : ss -ltnp | grep :%s\n' "${port}"
    exit 1
  fi
done

compose ps --format 'table {{.Name}}\t{{.Status}}' 2>/dev/null | sed 's/^/   /' || true
printf '\n'
ok "hạ tầng sẵn sàng"