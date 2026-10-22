#!/usr/bin/env bash
# Một lệnh để chạy toàn bộ test local.
#
#   ./scripts/e2e/run-all.sh          # dựng môi trường + chạy unit + integration + e2e
#   ./scripts/e2e/run-all.sh unit     # chỉ unit test (không cần backend)
#   ./scripts/e2e/run-all.sh e2e      # chỉ E2E (môi trường đã dựng sẵn)
#   ./scripts/e2e/run-all.sh down     # dừng tất cả

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FE_ROOT="$(cd "${REPO_ROOT}/../CAPSTONE_FE" && pwd)"
RUN_DIR="${REPO_ROOT}/scripts/e2e/.run"
PNPM=""
DOTNET=""
FE_PORT="${FE_PORT:-4010}"

say()  { printf '\n\033[1m== %s\033[0m\n' "$*"; }
ok()   { printf '   \033[32m%s\033[0m\n' "$*"; }
bad()  { printf '   \033[31m%s\033[0m\n' "$*"; }
log()  { printf '   → %s\n' "$*"; }

bring_up() {
  resolve_python
  resolve_toolchain
  # Clone mới chưa có .run/, mà các bước sau ghi log vào đó. Tạo trước để lần
  # chạy đầu tiên không chết ngay ở bước 1.
  mkdir -p "${RUN_DIR}"

  say "1/4 PostgreSQL 18 + Redis"
  # Giữ log thật: nuốt output khiến lỗi không hiện ra thì không ai sửa được.
  if ! "${REPO_ROOT}/scripts/e2e/bootstrap-postgres.sh" >"${RUN_DIR}/bootstrap.log" 2>&1; then
    bad "bootstrap-postgres thất bại:"
    sed 's/^/     /' "${RUN_DIR}/bootstrap.log"
    return 1
  fi
  ok "postgres + redis (docker)"

  say "2/4 Backend API + SMTP sink"
  # start-backend.sh từ chối chạy khi API của lần trước còn sống, nên phải dừng trước.
  # Dừng rồi mới bật, và phải kiểm tra exit code — nếu không, bước seed sẽ chạy
  # với backend đã chết rồi báo lỗi khó hiểu.
  "${REPO_ROOT}/scripts/e2e/stop-backend.sh" >/dev/null 2>&1 || true
  local log="${RUN_DIR}/start.log"
  if ! "${REPO_ROOT}/scripts/e2e/start-backend.sh" >"${log}" 2>&1; then
    bad "start-backend thất bại:"
    tail -n 12 "${log}" | sed 's/^/     /'
    return 1
  fi
  ok "API http://localhost:5191 + SMTP sink 127.0.0.1:2525"

  say "3/4 Seed seller"
  if ! "${REPO_ROOT}/scripts/e2e/seed-seller.sh" >"${RUN_DIR}/seed.log" 2>&1; then
    bad "seed-seller thất bại:"
    tail -n 12 "${RUN_DIR}/seed.log" | sed 's/^/     /'
    return 1
  fi
  ok "seller đã tạo qua flow thật (register → verify → login)"

  say "4/4 Frontend (production build)"
  if curl -sf -o /dev/null --max-time 3 "http://localhost:${FE_PORT}"; then
    ok "đã có server trên ${FE_PORT} — dùng lại, khỏi build"
    return 0
  fi

  if [ ! -d "${FE_ROOT}/node_modules" ]; then
    bad "CAPSTONE_FE chưa cài dependency:"
    printf '     cd %s && pnpm install\n' "${FE_ROOT}"
    return 1
  fi

  # next start cần .next có sẵn, và PORT phải khớp với E2E_BASE_URL — Next mặc
  # định là 3000, còn Playwright trỏ 4010.
  log "building frontend (lần đầu mất vài phút)"
  if ! (cd "${FE_ROOT}" && "${PNPM}" build) >"${RUN_DIR}/fe-build.log" 2>&1; then
    bad "pnpm build thất bại:"
    tail -n 15 "${RUN_DIR}/fe-build.log" | sed 's/^/     /'
    return 1
  fi

  log "starting frontend trên port ${FE_PORT}"
  (cd "${FE_ROOT}" \
    && PORT="${FE_PORT}" \
       NEXT_PUBLIC_API_BASE_URL="${E2E_API_BASE_URL:-http://localhost:5191}" \
       NEXT_PUBLIC_GOOGLE_CLIENT_ID="" \
       nohup "${PNPM}" start >"${RUN_DIR}/fe.log" 2>&1 &)

  for _ in $(seq 1 60); do
    curl -sf -o /dev/null --max-time 2 "http://localhost:${FE_PORT}" && break
    sleep 1
  done

  if curl -sf -o /dev/null --max-time 3 "http://localhost:${FE_PORT}"; then
    ok "frontend sẵn sàng ở http://localhost:${FE_PORT}"
  else
    bad "frontend không lên — xem ${RUN_DIR}/fe.log"
    return 1
  fi
}
die()  { printf '   \033[1;31mFATAL\033[0m %s\n' "$*" >&2; exit 1; }

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

# Không ghim đường dẫn của máy nào cả: mỗi người cài toolchain ở chỗ khác nhau.
resolve_toolchain() {
  local candidate
  for candidate in pnpm; do
    if command -v "${candidate}" >/dev/null 2>&1; then PNPM="$(command -v "${candidate}")"; return 0; fi
  done
  for candidate in "${HOME}/.local/share/pnpm/pnpm" "/c/Users/${USERNAME:-}/AppData/Local/pnpm/pnpm.exe"; do
    [ -x "${candidate}" ] && { PNPM="${candidate}"; return 0; }
  done
  if command -v corepack >/dev/null 2>&1; then PNPM="corepack pnpm"; return 0; fi
  die "không tìm thấy pnpm. Cài: npm i -g pnpm   (hoặc: corepack enable pnpm)"

  for candidate in dotnet; do
    if command -v "${candidate}" >/dev/null 2>&1; then DOTNET="$(command -v "${candidate}")"; return 0; fi
  done
  for candidate in \
    "/c/Program Files/dotnet/dotnet.exe" "/usr/share/dotnet/dotnet" "${HOME}/.dotnet/dotnet"; do
    [ -x "${candidate}" ] && { DOTNET="${candidate}"; return 0; }
  done
  die "không tìm thấy dotnet. Cài .NET 8 SDK: https://dotnet.microsoft.com/download"
}




load_credentials() {
  [ -f "${RUN_DIR}/seller.json" ] || return 1
  eval "$(${PYTHON} -c "
import json
d = json.load(open('${RUN_DIR}/seller.json'))
print('export E2E_SELLER_EMAIL=%s' % d['email'])
print('export E2E_SELLER_PASSWORD=%s' % d['password'])
")"
}

run_unit() {
  resolve_toolchain
  say "FE unit test"
  # tail cắt mất dòng summary vì vitest in thêm gợi ý tối ưu ở cuối, nên grep đúng hai dòng kết quả.
  (cd "${FE_ROOT}" && "${PNPM}" test 2>&1 \
    | grep -E "^[[:space:]]+(Test Files|Tests)[[:space:]]" | sed 's/^[[:space:]]*/   /')
}

run_be_unit() {
  resolve_toolchain
  say "BE unit test + coverage gate"
  (cd "${REPO_ROOT}" && DOTNET_ROOT="${HOME}/.dotnet" \
    && "${DOTNET}" test Capstone.sln -c Debug 2>&1 | grep -E "Passed!|Failed!" | sed 's/^/   /')
}

run_be_integration() {
  resolve_toolchain
  say "BE integration test (database riêng, tự wipe mỗi class)"
  if ! curl -sf -o /dev/null --max-time 3 http://localhost:5191/health; then
    bad "backend chưa chạy — dùng 'run-all.sh' không có tham số để dựng"; return 1
  fi
  # Database riêng: apcs_e2e là nơi E2E chạy thật, dùng chung sẽ làm các test
  # đếm/so sánh sai.
  eval "$("${REPO_ROOT}/scripts/e2e/bootstrap-test-db.sh" 2>/dev/null | grep '^export ')" \
    || { bad "bootstrap-test-db thất bại"; return 1; }
  (cd "${REPO_ROOT}" && DOTNET_ROOT="${HOME}/.dotnet" \
    && "${DOTNET}" test Capstone.sln -c Debug 2>&1 \
      | grep -E "Passed!|Failed!|error" | sed 's/^/   /')
}

run_e2e() {
  resolve_toolchain
  say "FE E2E"
  load_credentials || { bad "chưa có seller.json — chạy 'seed-seller.sh' trước"; return 1; }

  # Headed chỉ bật khi có màn hình; CI và shell không có display thì chạy headless.
  local browser_flag=""
  if [ "${E2E_HEADED:-}" = "1" ]; then
    browser_flag="--headed --workers=1"
    log "chạy headed (cần display; nếu không có thì đặt DISPLAY)"
  fi

  (cd "${FE_ROOT}" && "${PNPM}" test:e2e ${browser_flag} 2>&1 \
    | grep -E "passed|failed|flaky|skipped" | sed 's/^[[:space:]]*/   /')
}

run_all_tests() {
  local failures=()

  run_unit || failures+=("FE unit")
  run_be_integration || failures+=("BE unit + integration")
  # Từng tầng chạy độc lập: một tầng đỏ không được che các tầng còn lại.
  run_e2e || failures+=("FE E2E")

  if [ "${#failures[@]}" -gt 0 ]; then
    bad "tầng đỏ: ${failures[*]}"
    return 1
  fi

  ok "cả 4 tầng xanh"
}

case "${1:-all}" in
  down)   say "dừng tất cả"; "${REPO_ROOT}/scripts/e2e/stop-backend.sh" --all ;;
  unit)   run_unit; run_be_unit ;;
  e2e)    bring_up && run_e2e ;;
  integ)  bring_up && run_be_integration ;;
  all)    bring_up && run_all_tests ;;
  *)      bad "tham số không hợp lệ: $1 (all|unit|e2e|integ|down)"; exit 1 ;;
esac