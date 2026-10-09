# E2E backend environment

Brings up the stack Playwright E2E needs: a real API, a real PostgreSQL schema, a real Redis, and
an SMTP sink that keeps the verification email instead of sending it. No Docker, no production
service is contacted.

```
start-backend.sh   -> PostgreSQL, Redis, apcs_e2e schema, SMTP sink, the API on :5191
seed-seller.sh     -> one verified Seller account + its session cookies as JSON
stop-backend.sh    -> stops the API and the sink; keeps the database
```

## Order of operations

```bash
cd ~/fpt/.worktrees/be-e2e-env

./scripts/e2e/start-backend.sh          # idempotent; safe to re-run
./scripts/e2e/seed-seller.sh            # prints JSON on stdout, logs on stderr
./scripts/e2e/stop-backend.sh           # add --all to also stop PostgreSQL and Redis
```

`start-backend.sh` writes `scripts/e2e/.run/env.sh` on success. `seed-seller.sh` sources it
automatically, so the two only need to be run from the same worktree.

## Ports and paths

| What | Where |
|---|---|
| API | `http://localhost:5191` (health: `/health`) |
| PostgreSQL | `127.0.0.1:55432`, database `apcs_e2e`, user `apcs_e2e` |
| Redis | `127.0.0.1:56379` |
| SMTP sink | `127.0.0.1:2525` |
| Captured mail | `scripts/e2e/.run/mail.jsonl` (one JSON object per line) |
| Seeded account | `scripts/e2e/.run/seller.json` |
| Logs | `scripts/e2e/.run/{api,smtp,build,schema,postgres}.log` |

Use `http://localhost:5191`, not `127.0.0.1:5191`. The session cookies carry the `__Host-` prefix
and a `Secure` attribute, so a browser will only attach them to the `localhost` origin the client
runs on.

## The seller account

`seed-seller.sh` prints one JSON document to stdout (everything else goes to stderr):

```json
{
  "email": "seller.1791549384.140815@apcs.test",
  "password": "Passw0rdTest1!",
  "baseUrl": "http://localhost:5191",
  "mode": "api",
  "role": "Seller",
  "cookies": { "access": "...", "refresh": "..." },
  "cookieNames": {
    "access": "__Host-apcs_access", "refresh": "__Host-apcs_refresh",
    "httpOnly": true, "secure": true, "sameSite": "Lax", "path": "/", "domain": "localhost"
  }
}
```

Both cookies are `HttpOnly`; the script asserts that attribute before it prints anything, because
an E2E setup that reads the token from JavaScript would be testing a different contract than the
product has. Playwright turns `cookies` straight into `storageState` entries with
`{ name, value, domain: "localhost", path: "/", httpOnly: true, secure: true, sameSite: "Lax" }`.

Two modes, chosen with `E2E_SEED_MODE`:

- **`api` (default)** — `POST /api/auth/register`, read the verification token out of
  `mail.jsonl`, `POST /api/auth/verify-email`, then `POST /api/auth/login`. The account exists the
  only way a user could create it.
- **`sql`** — inserts `roles`, `users` and `user_roles` through `psql` and then logs in over HTTP
  so the cookies are still issued by the real pipeline. Faster, and it skips the SMTP sink.

Both end in a real login, so both prove the password hash is accepted by
`IPasswordHasher<User>`. `identity_hash.py` reproduces that hash outside .NET: PBKDF2-HMAC-SHA512,
100000 iterations, 16-byte salt, 32-byte subkey, packed behind big-endian integers.

## Resetting

| Goal | Command |
|---|---|
| Drop all captured mail | `./scripts/e2e/start-backend.sh` (passes `--reset` to the sink) |
| Drop the database and rebuild the schema | `dropdb -h 127.0.0.1 -p 55432 -U apcs apcs_e2e` then `start-backend.sh` |
| Start from an empty PostgreSQL datadir | point `E2E_PG_DATA_DIR` at a fresh `initdb` output |
| Rebuild the API from scratch | `rm -rf API/bin API/obj` then `start-backend.sh` |

The schema is only loaded when `information_schema.tables` reports fewer than 40 tables in
`public`, so re-running the script never re-applies the dump over live data.

## Proving nothing real was contacted

Four layers, all active by default:

1. **Credentials are blanked before the host boots.** `DotEnvLoader.Load()` skips any key that
   already exists in the process environment, and an empty string counts as existing, so
   `Authentication__Google__ClientId`, `Cloudinary__*` and `PayOS__*` are exported empty and the
   developer's `.env` values never enter the process.
2. **The adapters refuse locally.** `CloudinaryClientFactory.Create` throws
   `Cloudinary storage is not configured`, and `GoogleAuthService` rejects every token when the
   client id is blank. A support-ticket upload answers `500 system.unexpected` with that message
   and opens no socket.
3. **SMTP points at the sink.** `Smtp__Host=127.0.0.1`, `Smtp__Port=2525`,
   `Smtp__UseStartTls=false`. `Smtp__Username` and `Smtp__Password` must be non-empty or
   `SmtpOptions.IsConfigured` is false and `EmailService` takes its "log only" branch, which is the
   bug B3 the sink exists to avoid.
4. **Outbound HTTP is proxied into a black hole.** `PayOsGatewayClient` has no unconfigured guard,
   so `ALL_PROXY=http://127.0.0.1:1` with `NO_PROXY=localhost,127.0.0.1` makes any outbound
   `HttpClient` call fail while loopback stays direct. Disable with `E2E_PROXY_GUARD=off` only if
   you know what you are doing.

To check after a run:

```bash
ss -tanp | grep APCS.Api | grep -vE '127\.0\.0\.1|\[::1\]'   # expect no output
grep -iE 'cloudinary|payos|accounts\.google|smtp\.gmail' scripts/e2e/.run/api.log
```

`ASPNETCORE_ENVIRONMENT` is `E2E`, not `Development`, so the `/api/dev/*` endpoints that call PayOS
are not mapped.

## Environment variables

Everything is optional; the defaults are what E2E should use.

| Variable | Default | Meaning |
|---|---|---|
| `E2E_RUN_DIR` | `scripts/e2e/.run` | pid files, logs, `mail.jsonl`, `seller.json` |
| `E2E_PG_PORT` | `55432` | PostgreSQL port |
| `E2E_PG_DATA_DIR` | `/tmp/opencode/pgdata18` | must be a **PG 18** datadir |
| `E2E_PG_ROOT` | `/tmp/opencode/pg18` | root of the extracted `postgresql-18` packages |
| `E2E_DB` / `E2E_DB_USER` / `E2E_DB_PASSWORD` | `apcs_e2e` | database and role the API connects as |
| `E2E_PG_SUPERUSER` / `E2E_PG_SUPERPASSWORD` | `apcs` | superuser used to create the role and database |
| `E2E_REDIS_PORT` | `56379` | Redis port |
| `E2E_SMTP_PORT` / `E2E_SMTP_FILE` | `2525`, `.run/mail.jsonl` | SMTP sink |
| `E2E_API_PORT` | `5191` | API port |
| `E2E_API_ENVIRONMENT` | `E2E` | `ASPNETCORE_ENVIRONMENT`; `Development` would map `/api/dev/*` |
| `E2E_APP_BASE_URL` | `http://localhost:4010` | builds the verification link in the email |
| `E2E_CORS_ORIGINS` | `http://localhost:4010,http://localhost:4020` | Playwright and dev FE ports |
| `E2E_JWT_SIGNING_KEY` | test-only literal, ≥32 bytes | rejected by the API if shorter |
| `E2E_PROXY_GUARD` | `on` | set `off` to let outbound HTTP leave the machine |
| `E2E_SEED_MODE` | `api` | `api` or `sql` |
| `E2E_SELLER_EMAIL` / `E2E_SELLER_PASSWORD` / `E2E_SELLER_FULL_NAME` | generated / `Passw0rdTest1!` / `E2E Seller` | seeded credentials |
| `E2E_LOGIN_WAIT_SECONDS` | `90` | how long `seed-seller.sh` waits for the verification mail |

The password must satisfy `RegisterValidator`: at least 8 characters with an uppercase letter, a
lowercase letter and a digit.

## Notes and gotchas

- **PostgreSQL must be 18.** `scripts/sql/000-bootstrap-schema.sql` sets `transaction_timeout`,
  which PG 16 and 17 reject. `start-backend.sh` strips `\restrict` / `\unrestrict` before piping
  the dump into `psql`, because the local client is not always the same major version as the server.
- **`UseHttpsRedirection` is still in the pipeline.** With no HTTPS port configured the middleware
  logs `Failed to determine the https port for redirect` and passes HTTP through, so `:5191` stays
  plain HTTP.
- **A closed loopback port drops the SYN on this host** instead of refusing it, so every port probe
  uses a socket with an explicit one second timeout rather than bash's `/dev/tcp`.
- **`stop-backend.sh` keeps PostgreSQL and Redis running by default**, because the integration
  test suite shares those endpoints. Pass `--all` to stop them.
- The API is started with `dotnet build` followed by `dotnet run --no-build`, so the NuGet restore
  happens once, in the foreground, and never from the long-running process.