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
| `postgres-dev` | `pgvector/pgvector:pg18`, SSL, **own compose project `apcs-dev`** (`deploy/dev-db/compose.yml`) | port 5432, the shared dev database; no release touches it |
| `backup` | built from `deploy/backup` | nothing; runs `pg_dump` nightly |
| `dozzle` | `amir20/dozzle:v11` | `logs.<domain>` behind basic auth; reads the Docker socket read-only |
| `uptime-kuma` | `louislam/uptime-kuma:2` | `status.<domain>` behind basic auth (plus its own login) |

Caddy routes one hostname by path: `/api/*`, `/hubs/*`, and `/health` go to the API, `/internal/*`
answers 404, and everything else goes to the frontend. Frontend and API therefore share an origin,
which the host-only `__Host-apcs_access` cookie and the frontend's server-side session check rely
on. SSE is not buffered (`flush_interval -1`) and long AI calls get a 10-minute timeout.
`api.<domain>` is an alias of `<domain>/api` that serves only the API (for Postman, documentation
and external callbacks): `api.<domain>/auth/login` reaches `/api/auth/login`, while `/health`,
`/hubs/*` and already prefixed `/api/*` paths pass through unchanged. The browser app keeps using
`<domain>/api` so the session cookies stay on one host. The Etsy OAuth callback is set by
`Etsy__OAuthRedirectUri` in `deploy/env/api.env.tpl` (now `https://<domain>/api/etsy/oauth/callback`);
`https://api.<domain>/etsy/oauth/callback` reaches the same route, so switching only needs that value
changed together with the Callback URL registered on the Etsy app.

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
| Secret Manager `apcs-prod-secrets`, `apcs-prod-external`, `apcs-dev-secrets`, `apcs-dev-migrator-password` | all secret values (JSON objects except the last) |
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
| Secret `apcs-prod-secrets` | JSON of generated values nobody edits by hand: database, Redis, JWT and media-worker secrets |
| Secret `apcs-prod-external` | JSON of the external keys people upload as a whole file: Google client ID, SMTP, Cloudinary, PayOS, Gemini, assistant keys (`;`-separated, rotated on rate limits), MiniMax, Etsy, translation, Google Drive token, Discord webhook |
| Secret `apcs-ops-auth` | basic-auth user, password, and the base64 bcrypt hash Caddy checks for `logs.` and `status.` |
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

1. DNS: `A @`, `A www`, `A api`, `A logs` and `A status` → `<static IP>`.
2. Change `APP_DOMAIN` (backend) and `APP_DOMAIN`, `NEXT_PUBLIC_API_BASE_URL`, `NEXT_PUBLIC_SITE_URL` (frontend), merge into `production`, tag both repositories.
3. Add `https://<domain>` to the Google OAuth client's authorized JavaScript origins and point the PayOS webhook at `https://<domain>/api/subscriptions/webhooks/payos`.

## Releasing

```
integration branch ──PR──▶ production ──git tag vX.Y.Z && git push origin vX.Y.Z──▶ deploy-prod.yml
```

To release, tag the head of `production` with the same `vX.Y.Z` in both `3h4524/CAPSTONE_BE` and
`3h4524/CAPSTONE_FE` (GitHub → Releases → Draft a new release → new tag, target `production`, or
`git tag` + `git push`). Both deploy workflows start at once and build in parallel; `release.sh`
takes a lock on the VM, so when both reach the release step the second waits for the first instead
of running alongside it.

There is no approval step: a `v*` tag whose commit is on `production` deploys, and each repository
is tagged on its own (the versions do not have to match). Every release touches only its own part:

| Command on the VM | Run by | Restarts |
|---|---|---|
| `release.sh fe <tag>` | frontend tag | `fe` only |
| `release.sh api <tag>` | backend tag | renders the env files, takes a pre-deploy backup, runs `flyway migrate`, then restarts `api` and `media-worker` only |
| `release.sh infra` | backend tag when `deploy/compose.yml`, `deploy/Caddyfile`, `deploy/backup`, `deploy/postgres` or an infrastructure env template changed since the previous tag, or by hand (`workflow_dispatch`, target `infra`) | recreates Dozzle, Uptime Kuma and the backup container when their definition changed and reloads Caddy in place (deployment files are synced with `rsync --inplace` because Caddy bind-mounts the single `Caddyfile`, and a replaced file would leave it reading the old one; if the mounted copy still differs, Caddy is restarted instead); Postgres and Redis are only started when stopped, never recreated |
| `dev-db.sh up` | by hand | the shared dev database (project `apcs-dev`), only when someone runs it |

`deploy.sh` rolls the image back by itself when the new container does not become healthy. A lock
on the VM makes concurrent releases wait for each other. When an API change is needed by the
frontend, release the backend first or keep the API backward compatible.

Migrations are never rolled back. To return to an older release, run the workflow by hand with the
older tag selected as the ref (images are reused, migrations already applied are kept), or run
`deploy/scripts/rollback.sh api` on the VM.

The repository variable `DEV_MIGRATION_BRANCH` names the integration branch whose pushes migrate the
shared dev database (`migrate-dev.yml`). No GitHub secret is needed.

## Observability

| Tool | Where | What |
|---|---|---|
| Dozzle | `https://logs.<domain>` | live logs of every container, searchable, grouped by the `dev.dozzle.group` label (`app`, `data`, `edge`, `ops`) |
| Uptime Kuma | `https://status.<domain>` | checks the site, `/health`, the certificate, and the dev database every minute and alerts Discord |
| Cloud Monitoring (Ops Agent on the VM) | Google Cloud console → Monitoring | CPU, memory, disk, network; email alerts from `deploy/provision/create-alerts.sh` (CPU > 90% for 10 min, memory > 90%, disk > 85%) |

Both subdomains share one basic-auth login from the secret `apcs-ops-auth`
(`gcloud secrets versions access latest --secret apcs-ops-auth`). Caddy answers Dozzle's
`/sw.js` itself, without the login, with a worker that unregisters on activation: Dozzle's own service
worker refetches pages without the basic-auth credentials, so Chrome showed a bare
"HTTP ERROR 401" instead of the login prompt. A browser still holding the old worker recovers after one
reload.

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
3. Copy `deploy/` and `migrations/` to `/opt/apcs`, run `release.sh infra` and `dev-db.sh up`, restore the latest backups, and tag a release.
