# Database migrations (Flyway)

Every schema or catalog-data change is a SQL file in this folder. Nobody changes the shared dev
database or production by hand: only the `apcs_migrator` role can create, alter, or drop objects.
The `dev_<name>` logins hold `apcs_migrator` with `SET` but not `INHERIT`, so a plain session has
data rights only and a hand-written `ALTER TABLE` is refused, while Flyway started by
`scripts/Migrate-DevDb.ps1` switches to `apcs_migrator` and can apply migrations.

## Writing a migration

1. Create the file with `./scripts/New-Migration.ps1 -Name add_listing_language`. It is named
   `V<yyyyMMddHHmm>__<what_it_does>.sql` from the current Vietnam time, for example
   `V202611081030__add_listing_language.sql`, and is empty: Flyway does not generate SQL, so write
   the DDL yourself (try it on a local Postgres first if it is not trivial). Timestamps keep versions unique across branches;
   Flyway runs with `outOfOrder = true`, so a branch merged later still applies.
2. Never edit a migration that has been merged. Flyway checksums every applied file and refuses to
   run when one changes. Fix a mistake with a new migration.
3. Keep it backward compatible with the release still running (add first, remove in a later
   release). The database is never rolled back; only the application image is.
4. Seed or catalog data belongs here too, written idempotently
   (`INSERT ... ON CONFLICT DO NOTHING`).
5. Apply it to the shared dev database yourself and regenerate the entities in one go:

   ```powershell
   ./scripts/Migrate-DevDb.ps1 -Scaffold
   ```

   The script reads `ConnectionStrings__DefaultConnection` from the local `.env` (your `dev_<name>`
   login), runs Flyway in Docker with `SET ROLE apcs_migrator`, records your login as
   `installed_by`, and then runs `Scaffold-Database.ps1`. `-Command info` shows what is applied.
   Once a migration is on the dev database it is shared with everyone: do not edit it afterwards
   (write a new one), and if a branch is abandoned, write a migration that undoes its changes. If a
   file was edited anyway, `-Command repair` realigns the stored checksum.
6. `V1__baseline.sql` is the schema of the last Neon dump (2026-10-29) with every later
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
| A developer runs `scripts/Migrate-DevDb.ps1` | shared dev database | Flyway in Docker, as the developer's login with `SET ROLE apcs_migrator` |
| Push to the branch in the repository variable `DEV_MIGRATION_BRANCH` | shared dev database | `migrate-dev.yml` |
| Release tag on `production` | production | `deploy-prod.yml`, after a pre-deploy backup and before the new API starts |

Both dev paths ignore applied migrations whose file is not on the branch being migrated
(`ignoreMigrationPatterns = *:missing,*:future`), because a developer may apply a migration before
its pull request is merged. Production keeps the strict default.

Commit the regenerated entities in the same pull request as the migration.

## Running Flyway by hand

```bash
export FLYWAY_URL=jdbc:postgresql://127.0.0.1:15432/apcs FLYWAY_USER=apcs_migrator FLYWAY_PASSWORD=...
./deploy/scripts/flyway.sh info
```
