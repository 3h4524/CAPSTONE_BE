# Deployment

APCS runs on one cloud VM (Google Cloud by default, Azure as an alternative) with Docker Compose behind one domain. Everything needed to rebuild the
VM lives in [`deploy/`](../deploy); secrets never enter the repository.

## Topology

| Service | Image | Reachable from |
|---|---|---|
| `caddy` | `caddy:2` | Internet, ports 80/443; issues the Let's Encrypt certificate |
| `fe` | `ghcr.io/3h4524/apcs-fe:<tag>` | Caddy only |
| `api` | `ghcr.io/3h4524/apcs-api:<tag>` (`API/Dockerfile`) | Caddy and `media-worker` only |
| `media-worker` | `ghcr.io/3h4524/apcs-media-worker:<tag>` (`media-worker/Dockerfile`) | nothing; it polls the API |
| `redis` | `redis:7-alpine`, AOF and password | Docker network only |
| `postgres-prod` | `pgvector/pgvector:pg18` | Docker network, and `127.0.0.1:2510` on the VM |
| `postgres-dev` | `pgvector/pgvector:pg18`, SSL | port 5432, the shared dev database |
| `backup` | built from `deploy/backup` | nothing; runs `pg_dump` nightly |

Caddy routes one hostname by path: `/api/*`, `/hubs/*`, and `/health` go to the API, `/internal/*`
answers 404, and everything else goes to the frontend. Frontend and API therefore share an origin,
which the host-only `__Host-apcs_access` cookie and the frontend's server-side session check rely
on. SSE is not buffered (`flush_interval -1`) and long AI calls get a 10-minute timeout.

The API runs exactly one instance: its work queues are in-memory channels, its scheduled jobs have
no distributed lock, and SignalR has no backplane.

## Configuration

| File on the VM | Template |
|---|---|
| `/opt/apcs/deploy/.env` (domain, image tags) | `deploy/.env.example` |
| `/opt/apcs/env/api.env` | `deploy/env/api.env.example` |
| `/opt/apcs/env/media-worker.env` | `deploy/env/media-worker.env.example` |
| `/opt/apcs/env/redis.env` | `deploy/env/redis.env.example` |
| `/opt/apcs/env/postgres-prod.env`, `postgres-dev.env` | `deploy/env/postgres-*.env.example` |
| `/opt/apcs/env/backup.env` | `deploy/env/backup.env.example` |
| `/opt/apcs/env/tls/` | created by `deploy/provision/bootstrap-vm.sh` for the dev database |

Production-specific rules:

- `ASPNETCORE_ENVIRONMENT=Production`. `Development` would enable Swagger and the anonymous
  `/api/dev/*` endpoints.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so client IPs come from Caddy's `X-Forwarded-For`
  (the API is reachable only through Caddy).
- `ApiKeys__KeyRingPath=/data/keyring` on the `keyring` volume. Losing that volume makes every
  saved seller API key undecryptable, so it is part of the backup.
- The ONNX models download once into the `models` volume on first start.
- `RateLimiting__*` tunes the app-wide limiter (`API/Extensions/ApiRateLimiting.cs`): per user,
  per anonymous IP, and stricter per-IP limits on the sign-in, registration, password, and OTP
  endpoints. `/health`, `/internal/*`, `/hubs/*`, and the PayOS webhook are exempt. The limiter runs
  after authentication and before authorization, so unauthenticated calls to protected endpoints
  count too.
- Admin "Run tests" (TestReports), the OCR text check, and the video-quality worker need tools that
  the runtime image does not contain; keep them disabled.

## Without a domain

The browser only keeps the `Secure` session cookies over HTTPS, so the app cannot run on a bare
`http://<ip>`. Until a domain is bought, set `APP_DOMAIN=<ip>.sslip.io`: that name resolves to the
IP and Caddy obtains a Let's Encrypt certificate for it. Switching to a real domain later means
changing `APP_DOMAIN`, `App__BaseUrl`, `Cors__AllowedOrigins`, the frontend build variables, the
Google OAuth origins, and the PayOS webhook. The dev database needs no domain: developers connect
to `<ip>:5432`.

## Databases and roles

Each Postgres container runs `deploy/postgres/init/01-roles.sh` on first start:

| Role | Rights | Used by |
|---|---|---|
| `postgres` | superuser | operators, from inside the VM |
| `apcs_migrator` | owns the database, DDL | Flyway in CI |
| `apcs_app` (prod) | read/write data | the API |
| `dev_rw` (dev) + `dev_<name>` logins | read/write data | developers, created with `deploy/scripts/pg-dev-user.sh add <name>` |
| `apcs_backup` | `pg_read_all_data` | the backup container |

Schema changes follow [`migrations/README.md`](../migrations/README.md).

## Releasing

```
integration branch ──PR──▶ production ──git tag vX.Y.Z && git push origin vX.Y.Z──▶ deploy-prod.yml
```

`deploy-prod.yml` refuses a tag that is not on `production`, runs the tests, pushes the images,
waits for approval on the `production` environment, then on the VM: syncs `deploy/`, takes a
pre-deploy backup, runs `flyway migrate`, and rolls out with `deploy/scripts/deploy.sh`, which
rolls the image back by itself when the API does not become healthy. Migrations are never rolled
back. To redeploy an older release, run the workflow by hand with that tag (migrations are skipped)
or run `deploy/scripts/rollback.sh api` on the VM.

### GitHub settings

| Kind | Name | Scope |
|---|---|---|
| Variable | `DEV_MIGRATION_BRANCH` | repository; the integration branch whose pushes migrate the dev database |
| Variable | `APP_DOMAIN` | environment `production` |
| Secret | `PROD_SSH_HOST`, `PROD_SSH_USER`, `PROD_SSH_KEY`, `PROD_SSH_KNOWN_HOSTS` | environment `production` |
| Secret | `FLYWAY_PROD_PASSWORD` | environment `production` |
| Secret | `DEV_SSH_HOST`, `DEV_SSH_USER`, `DEV_SSH_KEY`, `DEV_SSH_KNOWN_HOSTS`, `FLYWAY_DEV_PASSWORD` | repository |

The `production` environment should require a reviewer and allow only `v*` tags.

## Backups

The `backup` container runs `deploy/backup/backup.sh` every day at 02:00 (Asia/Ho_Chi_Minh): a
`pg_dump -Fc` of both databases, the Data Protection key ring, and a Redis snapshot, verified with
`pg_restore --list`, uploaded with rclone to every remote in `BACKUP_REMOTES` (Azure Blob and Google
Drive), and reported to Discord. On Google Cloud the bucket is written with the VM's own service
account (`RCLONE_CONFIG_GCS_ENV_AUTH=true`), so no key is stored. It keeps 7 daily and 4 weekly sets; pre-deploy sets are kept for
30 days. Every Sunday at 03:00, `deploy/scripts/restore-test.sh` restores the latest production
dump into a throwaway container and reports the result.

Restore with `deploy/scripts/restore-db.sh <prod|dev> <file.dump>` and
`deploy/scripts/restore-keyring.sh <keyring.tgz>`.

## Working against the shared dev database

- Every developer's API runs the same background services against the same rows. Claims are
  atomic, so two machines never process one job at once, but a job left unfinished by one machine
  for five minutes is resumed by another.
- Seller API keys are encrypted with the local Data Protection key ring. A key saved from one
  machine cannot be decrypted on another unless the team shares one dev key ring
  (`ApiKeys__KeyRingPath` pointing at the same files on every machine).

## Rebuilding the VM

1. `deploy/provision/create-gcp-resources.sh` (gcloud, idempotent) creates the static IP,
   firewall rules, backup bucket, a service account the VM uses to write backups, and the VM.
   `deploy/provision/create-azure-resources.sh` does the same on Azure.
2. `deploy/provision/bootstrap-vm.sh` hardens SSH, adds swap, installs Docker, creates the
   `deploy` user and the dev database certificate, and schedules the weekly restore test.
3. Copy `deploy/` and the env files, restore the latest backups, start the stack, and point DNS
   at the new IP.
