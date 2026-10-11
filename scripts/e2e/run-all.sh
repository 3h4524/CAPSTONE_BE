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
PNPM="/home/nhat/.nvm/versions/node/v22.20.0/bin/pnpm"
FE_PORT="${FE_PORT:-4010}"

say()  { printf '\n\033[1m== %s\033[0m\n' "$*"; }
ok()   { printf '   \033[32m%s\033[0m\n' "$*"; }
bad()  { printf '   \033[31m%s\033[0m\n' "$*"; }

bring_up() {
  resolve_python
  say "1/4 PostgreSQL 18 + Redis"
  "${REPO_ROOT}/scripts/e2e/bootstrap-postgres.sh" >/dev/null 2>&1 \
    && ok "postgres + redis (docker)" \
    || { bad "bootstrap-postgres thất bại — chạy nó thủ công để xem lỗi"; return 1; }

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
  else
    (cd "${FE_ROOT}" && NEXT_PUBLIC_API_BASE_URL=http://localhost:5191 NEXT_PUBLIC_GOOGLE_CLIENT_ID= \
      nohup "${PNPM}" start >"${RUN_DIR}/fe.log" 2>&1 &)
    for _ in $(seq 1 40); do
      curl -sf -o /dev/null --max-time 2 "http://localhost:${FE_PORT}" && break
      sleep 1
    done
    curl -sf -o /dev/null --max-time 3 "http://localhost:${FE_PORT}" \
      && ok "frontend sẵn sàng ở http://localhost:${FE_PORT}" \
      || { bad "frontend không lên — xem ${RUN_DIR}/fe.log (chạy 'pnpm build' trước)"; return 1; }
  fi
}

# Windows chỉ có lệnh "python"; các bản phân phối khác dùng "python3". Git Bash
# không tự ánh xạ giữa hai tên này, nên phải tự dò trước.
PYTHON=""
resolve_python() {
  PYTHON="$(command -v python3 || command -v python || true)"
  [ -n "${PYTHON}" ] || die "cần Python 3 (cài từ python.org hoặc: winget install Python.Python.3.12)"
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
  say "FE unit test"
  (cd "${FE_ROOT}" && "${PNPM}" test 2>&1 | tail -4)
}

run_be_unit() {
  say "BE unit test (707 test + coverage gate)"
  (cd "${REPO_ROOT}" && export DOTNET_ROOT="${HOME}/.dotnet" \
    && dotnet test Capstone.sln -c Debug 2>&1 | grep -E "Passed!|Failed!" | sed 's/^/   /')
}

run_be_integration() {
  say "BE integration test (database riêng, tự wipe mỗi class)"
  if ! curl -sf -o /dev/null --max-time 3 http://localhost:5191/health; then
    bad "backend chưa chạy — dùng 'run-all.sh' không có tham số để dựng"; return 1
  fi
  # Database riêng: apcs_e2e là nơi E2E chạy thật, dùng chung sẽ làm các test
  # đếm/so sánh sai.
  eval "$("${REPO_ROOT}/scripts/e2e/bootstrap-test-db.sh" 2>/dev/null | grep '^export ')" \
    || { bad "bootstrap-test-db thất bại"; return 1; }
  (cd "${REPO_ROOT}" && export DOTNET_ROOT="${HOME}/.dotnet" \
    && dotnet test Capstone.sln -c Debug 2>&1 \
      | grep -E "Passed!|Failed!|error" | sed 's/^/   /')
}

run_e2e() {
  say "FE E2E (54 test)"
  load_credentials || { bad "chưa có seller.json — chạy 'seed-seller.sh' trước"; return 1; }
  (cd "${FE_ROOT}" && "${PNPM}" test:e2e 2>&1 | tail -6)
}

case "${1:-all}" in
  down)   say "dừng tất cả"; "${REPO_ROOT}/scripts/e2e/stop-backend.sh" --all ;;
  unit)   run_unit; run_be_unit ;;
  e2e)    bring_up && run_e2e ;;
  integ)  bring_up && run_be_integration ;;
  all)    bring_up && run_unit && run_be_integration && run_e2e ;;
  *)      bad "tham số không hợp lệ: $1 (all|unit|e2e|integ|down)"; exit 1 ;;
esac