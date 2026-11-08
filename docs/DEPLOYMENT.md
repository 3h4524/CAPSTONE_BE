# Deployment

APCS runs on one Google Cloud VM (`vm-apcs-prod` in project `apcs-prod`, `asia-southeast1-b`) with
Docker Compose behind one domain. Everything needed to rebuild it lives in [`deploy/`](../deploy).
Secrets live only in Google Secret Manager; the repository holds templates and non-secret values.

## Topology

| Service | Image | Reachable from |
|---|---|---|
| `caddy` | `caddy:2` | Internet, ports 80/443; issues the Let's Encrypt certificate |
| `fe` | `asia-southeast1-docker.pkg.dev/apcs-prod/apcs/apcs-fe:<tag>` | Caddy only |
| `api` | `.../apcs-api:<tag>` (`API/Dockerfile`) | Caddy and `media-worker` only |
| `media-worker` | `.../apcs-media-worker:<tag>` (`media-worker/Dockerfile`) | nothing; it polls the API |
| `redis` | `redis:7-alpine`, AOF and password | Docker network only |
| `postgres-prod` | `pgvector/pgvector:pg18` | Docker network, and `127.0.0.1:2510` on the VM |
| `postgres-dev` | `pgvector/pgvector:pg18`, SSL | port 5432, the shared dev database |
| `backup` | built from `deploy/backup` | nothing; runs `pg_dump` nightly |

Caddy routes one hostname by path: `/api/*`, `/hubs/*`, and `/health` go to the API, `/internal/*`
answers 404, and everything else goes to the frontend. Frontend and API therefore share an origin,
which the host-only `__Host-apcs_access` cookie and the frontend's server-side session check rely
on. SSE is not buffered (`flush_interval -1`) and long AI calls get a 10-minute timeout.
`api.<domain>` is an alias that serves only the API (for Postman, documentation and external
webhooks); the browser app keeps using `<domain>/api` so the session cookies stay on one host.

The API runs exactly one instance: its work queues are in-memory channels, its scheduled jobs have
no distributed lock, and SignalR has no backplane.

## Google Cloud resources

Created by `deploy/provision/create-gcp-resources.sh` (idempotent, `gcloud` as a project owner).

| Resource | Purpose |
|---|---|
| Service account `apcs-vm` | attached to the VM: reads the three secrets, pulls images, writes backups |
| Service account `apcs-deployer` | GitHub Actions on a **tag push** of either repository (Workload Identity Federation): pushes images, SSHes in over IAP with OS Login admin |
| Service account `apcs-dev-migrator` | GitHub Actions on a **branch push** of the backend repository: opens an IAP tunnel to port 5432 and reads only `apcs-dev-migrator-password`; cannot SSH |
| Workload identity pool `github`, provider `github-actions` | accepts tokens only from `3h4524/CAPSTONE_BE` and `3h4524/CAPSTONE_FE` for `push` and `workflow_dispatch` (never pull requests); `attribute.repo_ref` = `<repo>@<tag\|branch>` selects the service account |
| Artifact Registry `asia-southeast1/apcs` | images; cleanup keeps the 5 newest versions |
| Secret Manager `apcs-prod-secrets`, `apcs-dev-secrets`, `apcs-dev-migrator-password` | all secret values (JSON objects for the first two) |
| Bucket `gs://apcs-backups-64e51c` | backups, deleted after 35 days |
| Firewall | `apcs-allow-web` 80/443 from anywhere; `apcs-allow-iap` 22 and 5432 from the IAP range `35.235.240.0/20`; `apcs-allow-dev-postgres` 5432 from anywhere until go-live; the default `default-allow-ssh` and `default-allow-rdp` rules are deleted |
| VM | `e2-standard-2`, Ubuntu 24.04, 64 GB pd-balanced, static IP, OS Login on, no SSH keys in metadata |

No service account key exists anywhere (the organization enforces
`iam.disableServiceAccountKeyCreation`). People reach the VM with
`gcloud compute ssh vm-apcs-prod --zone asia-southeast1-b --tunnel-through-iap --project apcs-prod`;
port 22 is not open to the internet.

## Configuration and secrets

| File | Content |
|---|---|
| `deploy/env/common.env` | non-secret settings shared by scripts and workflows: project, zone, VM, registry, backup bucket, `APP_DOMAIN` |
| `deploy/env/*.env.tpl` | one template per service; plain values are written in, secrets are `${NAME}` |
| Secret `apcs-prod-secrets` | JSON with every `${NAME}` the templates use: database, Redis, JWT and media-worker secrets, and the external keys (Cloudinary, PayOS, SMTP, Google client ID, Gemini, assistant, MiniMax, Etsy, translation, Google Drive token, Discord webhook) |
| Secret `apcs-dev-secrets` | `DEV_POSTGRES_PASSWORD`, `DEV_BACKUP_PASSWORD` |
| Secret `apcs-dev-migrator-password` | the dev database `apcs_migrator` password |

`deploy/scripts/render-env.sh` runs on the VM at every release: it reads the secrets with the VM's
service account, fills the templates, and writes `/opt/apcs/env/*.env` and `/opt/apcs/deploy/.env`
(mode 600). A name missing from Secret Manager stops the release. To change a secret, add a new
version and redeploy (or run `render-env.sh` and restart the service).

Production-specific settings in `api.env.tpl`: `ASPNETCORE_ENVIRONMENT=Production` (Development
would enable Swagger and the anonymous `/api/dev/*` endpoints), `ASPNETCORE_FORWARDEDHEADERS_ENABLED`
(client IPs from Caddy), the Data Protection key ring on the `keyring` volume (part of the backup;
losing it makes every saved seller API key undecryptable), ONNX models on the `models` volume, and
the `RateLimiting__*` limits. The OCR text check, the video-quality worker, and admin "Run tests"
need tools the runtime image does not contain and stay disabled.

## Domain

`APP_DOMAIN` in `deploy/env/common.env` and the URLs in the frontend's `deploy/fe.build.env` are the
only places the public host name appears. Until a domain is bought it is `<ip>.sslip.io`, a name that
resolves to the VM's IP so Caddy can still obtain a certificate (the app needs HTTPS: the session
cookies are `Secure`). To move to a real domain:

1. DNS: `A @`, `A www` and `A api` → `<static IP>`.
2. Change `APP_DOMAIN` (backend) and `APP_DOMAIN`, `NEXT_PUBLIC_API_BASE_URL`, `NEXT_PUBLIC_SITE_URL` (frontend), merge into `production`, tag both repositories.
3. Add `https://<domain>` to the Google OAuth client's authorized JavaScript origins and point the PayOS webhook at `https://<domain>/api/subscriptions/webhooks/payos`.

## Releasing

```
integration branch ──PR──▶ production ──git tag vX.Y.Z && git push origin vX.Y.Z──▶ deploy-prod.yml
```

There is no approval step: a `v*` tag whose commit is on `production` deploys. Backend:
`deploy-prod.yml` checks the tag, runs the tests, builds and pushes `apcs-api` and
`apcs-media-worker`, copies `deploy/` and `migrations/` to the VM over IAP, and runs
`deploy/scripts/release.sh api <tag>`: render the env files, take a pre-deploy backup, `flyway
migrate` production (`deploy/scripts/migrate.sh`), roll out with `deploy/scripts/deploy.sh` (it rolls
the image back by itself when the API does not become healthy), and prune old images. Frontend: the
same with `release.sh fe <tag>`, build arguments from `deploy/fe.build.env`.

Migrations are never rolled back. To return to an older release, run the workflow by hand with the
older tag selected as the ref (images are reused, migrations already applied are kept), or run
`deploy/scripts/rollback.sh api` on the VM.

The repository variable `DEV_MIGRATION_BRANCH` names the integration branch whose pushes migrate the
shared dev database (`migrate-dev.yml`). No GitHub secret is needed.

## Databases and roles

Each Postgres container runs `deploy/postgres/init/01-roles.sh` on first start:

| Role | Rights | Used by |
|---|---|---|
| `postgres` | superuser | operators, from inside the VM |
| `apcs_migrator` | owns the database, DDL | Flyway |
| `apcs_app` (prod) | read/write data | the API |
| `dev_rw` (dev) + `dev_<name>` logins | read/write data | developers, created with `deploy/scripts/pg-dev-user.sh add <name>` |
| `apcs_backup` | `pg_read_all_data` | the backup container |

Schema changes follow [`migrations/README.md`](../migrations/README.md).

## Backups

The `backup` container runs `deploy/backup/backup.sh` every day at 02:00 (Asia/Ho_Chi_Minh): a
`pg_dump -Fc` of both databases, the Data Protection key ring, and a Redis snapshot, verified with
`pg_restore --list`, uploaded to the bucket (with the VM's service account) and to Google Drive when
a token is configured, and reported to Discord. It keeps 7 daily and 4 weekly sets; pre-deploy sets
are kept for 30 days. Every Sunday at 03:00, `deploy/scripts/restore-test.sh` restores the latest
production dump into a throwaway container and reports the result.

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

1. `deploy/provision/create-gcp-resources.sh`.
2. `gcloud compute ssh vm-apcs-prod --tunnel-through-iap --command 'sudo bash -s' < deploy/provision/bootstrap-vm.sh`.
3. Copy `deploy/` and `migrations/` to `/opt/apcs`, restore the latest backups, and tag a release.
