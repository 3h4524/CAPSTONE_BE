#!/usr/bin/env bash
# Creates one verified Seller account for E2E and prints its session cookies as JSON.
#
# Two modes, selected with E2E_SEED_MODE:
#   api  (default) — walks the real register -> verification email -> verify -> login flow, so the
#                    seeded account exists only the way a user could create it.
#   sql            — writes roles/users/user_roles straight through psql. Faster, but it needs the
#                    ASP.NET Core Identity password hash, which identity_hash.py reproduces.
#
# Every log line goes to stderr so stdout stays a single JSON document for the Playwright global
# setup to parse.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN_DIR="${E2E_RUN_DIR:-${SCRIPT_DIR}/.run}"
mkdir -p "${RUN_DIR}"

# shellcheck disable=SC1091
[ -f "${RUN_DIR}/env.sh" ] && . "${RUN_DIR}/env.sh"

BASE_URL="${E2E_BASE_URL:-http://localhost:5191}"
SMTP_FILE="${E2E_SMTP_FILE:-${RUN_DIR}/mail.jsonl}"
PG_PORT="${E2E_PG_PORT:-55432}"
E2E_DB="${E2E_DB:-apcs_e2e}"
E2E_DB_USER="${E2E_DB_USER:-apcs_e2e}"
E2E_DB_PASSWORD="${E2E_DB_PASSWORD:-apcs_e2e}"

SEED_MODE="${E2E_SEED_MODE:-api}"
SELLER_ROLE="${E2E_SELLER_ROLE_CODE:-Seller}"
EMAIL="${E2E_SELLER_EMAIL:-seller.$(date +%s).$$@apcs.test}"
# Matches RegisterValidator: at least 8 characters with an uppercase, a lowercase and a digit.
PASSWORD="${E2E_SELLER_PASSWORD:-Passw0rdTest1!}"
FULL_NAME="${E2E_SELLER_FULL_NAME:-E2E Seller}"
LOGIN_WAIT_SECONDS="${E2E_LOGIN_WAIT_SECONDS:-90}"

ACCESS_COOKIE="__Host-apcs_access"
REFRESH_COOKIE="__Host-apcs_refresh"

TMP_BODY="$(mktemp)"
TMP_HEADERS="$(mktemp)"
trap 'rm -f "${TMP_BODY}" "${TMP_HEADERS}"' EXIT

log()  { printf '\033[1;34m[seed]\033[0m %s\n' "$*" >&2; }
warn() { printf '\033[1;33m[seed]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[seed] FATAL\033[0m %s\n' "$*" >&2; exit 1; }

psql_run() {
  PGPASSWORD="${E2E_DB_PASSWORD}" psql -v ON_ERROR_STOP=1 -qtAX \
    -h 127.0.0.1 -p "${PG_PORT}" -U "${E2E_DB_USER}" -d "${E2E_DB}" -c "$1"
}

sink_line_count() {
  [ -f "${SMTP_FILE}" ] || { echo 0; return; }
  grep -c . "${SMTP_FILE}" || true
}

request() {
  # request METHOD PATH [JSON_BODY] -> writes headers to TMP_HEADERS, body to TMP_BODY, echoes status
  local method="$1" path="$2" body="${3:-}"
  local -a args=(-sS -D "${TMP_HEADERS}" -o "${TMP_BODY}" -w '%{http_code}'
                 -X "${method}" --max-time 20 -H 'Accept: application/json')

  [ -n "${body}" ] && args+=(-H 'Content-Type: application/json' -d "${body}")

  curl "${args[@]}" "${BASE_URL}${path}"
}

read_json_field() {
  python3 -c 'import json,sys
try:
    print(json.load(open(sys.argv[1], encoding="utf-8")).get(sys.argv[2], "") or "")
except Exception:
    print("")' "${TMP_BODY}" "$1"
}

read_set_cookie() {
  # Cookie values are extracted but never printed; only the name reaches the log.
  python3 -c 'import re,sys
headers = open(sys.argv[1], encoding="utf-8", errors="replace").read()
match = re.search(r"(?im)^set-cookie:\s*" + re.escape(sys.argv[2]) + r"=([^;]*)", headers)
print(match.group(1) if match else "")' "${TMP_HEADERS}" "$1"
}

cookie_is_httponly() {
  python3 -c 'import re,sys
headers = open(sys.argv[1], encoding="utf-8", errors="replace").read()
match = re.search(r"(?im)^set-cookie:\s*" + re.escape(sys.argv[2]) + r"=([^;]*)(.*)$", headers)
flags = match.group(2).lower() if match else ""
print("true" if "httponly" in flags else "false")' "${TMP_HEADERS}" "$1"
}

wait_for_verification_token() {
  # Polls the sink rather than reading once, because the API answers registration before MailKit
  # has finished handing the message over.
  local since="$1" target="$2" attempt
  for attempt in $(seq 1 "${LOGIN_WAIT_SECONDS}"); do
    local token
    token="$(python3 -c 'import json,re,sys,urllib.parse
path, since, target = sys.argv[1], int(sys.argv[2]), sys.argv[3].lower()
try:
    lines = open(path, encoding="utf-8").read().splitlines()
except FileNotFoundError:
    lines = []
for line in lines[since:]:
    if not line.strip():
        continue
    try:
        record = json.loads(line)
    except ValueError:
        continue
    recipients = [str(a).lower() for a in (record.get("to") or [])]
    recipients += [a.split(":", 1)[-1].strip("<> ").lower() for a in record.get("envelope", {}).get("rcptTo", [])]
    if target not in recipients:
        continue
    match = re.search(r"verify-email\?token=([A-Za-z0-9._~%+-]+)", record.get("body") or "")
    if match:
        print(urllib.parse.unquote(match.group(1)))
        break' "${SMTP_FILE}" "${since}" "${target}")"

    [ -n "${token}" ] && { printf '%s' "${token}"; return 0; }
    sleep 1
  done

  return 1
}

seed_via_api() {
  local before status token

  before="$(sink_line_count)"
  log "mode=api: registering ${EMAIL}"

  status="$(request POST /api/auth/register \
    "$(python3 -c 'import json,sys; print(json.dumps({"email":sys.argv[1],"password":sys.argv[2],"fullName":sys.argv[3]}))' \
      "${EMAIL}" "${PASSWORD}" "${FULL_NAME}")")"

  if [ "${status}" = "409" ]; then
    warn "address already registered; continuing to verification"
  elif [ "${status}" != "200" ]; then
    cat "${TMP_BODY}" >&2
    die "register answered ${status}, expected 200"
  fi

  log "mode=api: waiting for the verification email in the SMTP sink"
  token="$(wait_for_verification_token "${before}" "${EMAIL}")" \
    || die "no verification email for ${EMAIL} arrived at ${SMTP_FILE} within ${LOGIN_WAIT_SECONDS}s"

  log "mode=api: redeeming the verification token"
  status="$(request POST /api/auth/verify-email \
    "$(python3 -c 'import json,sys; print(json.dumps({"token":sys.argv[1]}))' "${token}")")"

  case "${status}" in
    204) log "mode=api: email verified" ;;
    401) die "the verification token was rejected as invalid or already spent" ;;
    *)   cat "${TMP_BODY}" >&2; die "verify-email answered ${status}, expected 204" ;;
  esac
}

seed_via_sql() {
  local hash

  hash="$(python3 "${SCRIPT_DIR}/identity_hash.py" "${PASSWORD}")" \
    || die "could not produce an Identity password hash"

  log "mode=sql: writing roles, users and user_roles"

  psql_run "
    INSERT INTO roles (id, code, name, is_system_role, created_at)
    VALUES (gen_random_uuid(), '${SELLER_ROLE}', '${SELLER_ROLE}', true, now())
    ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name;

    DELETE FROM user_roles WHERE user_id IN (SELECT id FROM users WHERE email = '${EMAIL}');
    DELETE FROM users WHERE email = '${EMAIL}';

    INSERT INTO users
      (id, email, password_hash, full_name, account_status, email_verified, email_verified_at, created_at, updated_at)
    VALUES
      (gen_random_uuid(), '${EMAIL}', '${hash}', '${FULL_NAME}', 'active', true, now(), now(), now());

    INSERT INTO user_roles (user_id, role_id, granted_at, revoked_at)
    SELECT u.id, r.id, now(), NULL
    FROM users u CROSS JOIN roles r
    WHERE u.email = '${EMAIL}' AND r.code = '${SELLER_ROLE}';
  " >/dev/null

  log "mode=sql: account inserted with role ${SELLER_ROLE} and email_verified=true"
}

login_and_capture_cookies() {
  local status access refresh

  log "signing in to capture the session cookies"
  status="$(request POST /api/auth/login \
    "$(python3 -c 'import json,sys; print(json.dumps({"email":sys.argv[1],"password":sys.argv[2]}))' \
      "${EMAIL}" "${PASSWORD}")")"

  if [ "${status}" != "200" ]; then
    cat "${TMP_BODY}" >&2
    die "login answered ${status}, expected 200"
  fi

  access="$(read_set_cookie "${ACCESS_COOKIE}")"
  refresh="$(read_set_cookie "${REFRESH_COOKIE}")"

  [ -n "${access}" ] || die "the login response set no ${ACCESS_COOKIE} cookie"
  [ -n "${refresh}" ] || die "the login response set no ${REFRESH_COOKIE} cookie"

  [ "$(cookie_is_httponly "${ACCESS_COOKIE}")" = "true" ] \
    || die "${ACCESS_COOKIE} is not HttpOnly, so it is not the cookie contract E2E depends on"
  [ "$(cookie_is_httponly "${REFRESH_COOKIE}")" = "true" ] \
    || die "${REFRESH_COOKIE} is not HttpOnly, so it is not the cookie contract E2E depends on"

  ACCESS_TOKEN="${access}"
  REFRESH_TOKEN="${refresh}"
  COOKIE_HTTPONLY=true
  # The final JSON is assembled by python, which reads these from its own environment.
  export ACCESS_TOKEN REFRESH_TOKEN COOKIE_HTTPONLY
}

main() {
  case "${SEED_MODE}" in
    api|sql) ;;
    *) die "unknown E2E_SEED_MODE '${SEED_MODE}'; expected 'api' or 'sql'" ;;
  esac

  command -v curl >/dev/null 2>&1 || die "curl is required"
  command -v python3 >/dev/null 2>&1 || die "python3 is required"
  [ "${SEED_MODE}" != "sql" ] || command -v psql >/dev/null 2>&1 || die "psql is required for E2E_SEED_MODE=sql"

  curl -sS -o /dev/null --max-time 5 "${BASE_URL}/health" \
    || die "the API is not answering on ${BASE_URL}/health; run scripts/e2e/start-backend.sh first"

  ACCESS_TOKEN=""
  REFRESH_TOKEN=""
  COOKIE_HTTPONLY=false

  if [ "${SEED_MODE}" = "api" ]; then seed_via_api; else seed_via_sql; fi
  login_and_capture_cookies

  # The password reaches stdout because the JSON document is the artefact E2E consumes; it is a
  # test-only credential for a loopback database and is never a real secret.
  python3 -c 'import json,os,sys
document = {
    "email": sys.argv[1],
    "password": sys.argv[2],
    "baseUrl": sys.argv[3],
    "mode": sys.argv[4],
    "role": sys.argv[5],
    "cookies": {
        "access": os.environ["ACCESS_TOKEN"],
        "refresh": os.environ["REFRESH_TOKEN"],
    },
    "cookieNames": {
        "access": "__Host-apcs_access",
        "refresh": "__Host-apcs_refresh",
        "httpOnly": os.environ["COOKIE_HTTPONLY"] == "true",
        "secure": True,
        "sameSite": "Lax",
        "path": "/",
        "domain": "localhost",
    },
}
sys.stdout.write(json.dumps(document, indent=2) + "\n")' \
    "${EMAIL}" "${PASSWORD}" "${BASE_URL}" "${SEED_MODE}" "${SELLER_ROLE}" \
    | tee "${RUN_DIR}/seller.json"

  log "seller.json written to ${RUN_DIR}/seller.json"
  log "cookies captured: ${ACCESS_COOKIE}, ${REFRESH_COOKIE} (both HttpOnly; values not printed)"
}

main "$@"