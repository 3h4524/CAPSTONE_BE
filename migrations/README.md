# Database migrations (Flyway)

Every schema or catalog-data change is a SQL file in this folder. Nobody changes the shared dev
database or production by hand: the dev logins have no DDL rights, and only the `apcs_migrator`
role used by CI can create, alter, or drop objects.

## Writing a migration

1. Name it `V<yyyyMMddHHmm>__<what_it_does>.sql`, using the current Vietnam time, for example
   `V202611081030__add_listing_language.sql`. Timestamps keep versions unique across branches;
   Flyway runs with `outOfOrder = true`, so a branch merged later still applies.
2. Never edit a migration that has been merged. Flyway checksums every applied file and refuses to
   run when one changes. Fix a mistake with a new migration.
3. Keep it backward compatible with the release still running (add first, remove in a later
   release). The database is never rolled back; only the application image is.
4. Seed or catalog data belongs here too, written idempotently
   (`INSERT ... ON CONFLICT DO NOTHING`).
5. `V1__baseline.sql` is the schema of the last Neon dump (2026-10-29) with every later
   `scripts/sql` delta applied, checked column for column against the EF model; it keeps the
   check constraints and expression indexes the EF model cannot express. `V2__seed_catalog.sql`
   holds the system catalog (roles, plans, features, allowances, system templates and presets,
   video templates, music, AI character models) as `INSERT ... ON CONFLICT DO NOTHING`, so it
   fills an empty production database and changes nothing on a database that already has it.
   A database restored from a dump is marked with `flyway baseline` (version 1) instead of
   running V1.

## Where it runs

| When | Database | How |
|---|---|---|
| Every pull request | empty `pgvector/pgvector:pg18` | `backend-ci.yml` job `migrations` applies all files from scratch |
| Push to the branch in the repository variable `DEV_MIGRATION_BRANCH` | shared dev database | `migrate-dev.yml` |
| Release tag on `production` | production | `deploy-prod.yml`, after a pre-deploy backup and before the new API starts |

After a migration reaches the dev database, regenerate the entities with
`./scripts/Scaffold-Database.ps1` and commit them in the same pull request.

## Running Flyway by hand

```bash
export FLYWAY_URL=jdbc:postgresql://127.0.0.1:15432/apcs FLYWAY_USER=apcs_migrator FLYWAY_PASSWORD=...
./deploy/scripts/flyway.sh info
```
