# APCS Repository Agent Guide

These instructions apply to the entire repository.

## Authority and precedence

1. The user's current request defines the task and its scope.
2. The checked-out source, project files, migrations, and tests define the current implementation.
3. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) defines architecture, CQRS, dependency, and placement rules.
4. [`CONTRIBUTING.md`](CONTRIBUTING.md) defines coding conventions and the feature-development workflow.
5. [`docs/TESTING.md`](docs/TESTING.md) defines test boundaries and conventions.
6. `.agent/` prompts, workflows, and skills are task aids. They must reference the sources above rather than override them.

When source and documentation disagree, do not silently follow either one. Report the discrepancy, preserve current behavior unless the task authorizes a change, and update documentation only when the intended rule is clear.

## What to read for a task

The list above resolves conflicts. This table decides reading order, so a task reads what it needs instead of everything.

| Task | Read before editing |
|---|---|
| Add or change a use case, endpoint, handler, or persistence access | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for dependency direction, CQRS, and type placement, then *Adding a new feature* in [`CONTRIBUTING.md`](CONTRIBUTING.md) |
| Add or change tests only | [`docs/TESTING.md`](docs/TESTING.md) |
| Change a layer boundary, a project reference, or add a dependency | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) |
| Rename or move types, or apply naming and language conventions | [`CONTRIBUTING.md`](CONTRIBUTING.md) |
| Change an entity or the schema | *Changing the database schema* below, [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for Domain and persistence rules, then *Database-first schema workflow* in [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`migrations/README.md`](migrations/README.md) |
| Change Docker images, the VM stack, CI/CD, or backups | [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) |
| Change the assistant chatbot or RAG caching | [`docs/ASSISTANT.md`](docs/ASSISTANT.md) and [`docs/adr/0001-rag-query-caching.md`](docs/adr/0001-rag-query-caching.md) |
| Prepare a pull request | *Definition of done* in [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`.github/pull_request_template.md`](.github/pull_request_template.md) |
| Use an APCS prompt or workflow | [`.agent/README.md`](.agent/README.md) |

A change that matches more than one row reads every row that applies.

## Local environment

The API reads its settings from the ignored `.env` in the repository root, and two of them bite
when the API runs next to the frontend.

- **`Cors__AllowedOrigins` separates its values with `;`, not `,`** and the name is
  case-sensitive. The configuration binder only reads the values of an array from the child keys,
  so an environment variable reaches `GetOptionalStringArray` as one string and the helper splits
  it on `;`. A comma-separated value arrives as one broken origin and every browser call is
  refused with "no Access-Control-Allow-Origin header".
- **In `Development` the environment variable is ignored.** `appsettings.Development.json` holds
  `Cors:AllowedOrigins` as an array, and those child keys win over the environment variable, so
  only `http://localhost:3000` is allowed. To allow another origin in `Development`, add it to
  that file or run the API with another environment (the E2E stack uses `E2E`).
- The local `.env` points at the **shared dev database** on the deployment VM (a personal
  `dev_<name>` login over SSL with data rights only), not at a local Postgres: the rows of every
  run and every account land there, and schema changes go through `migrations/`, applied with
  `scripts/Migrate-DevDb.ps1` (Flyway switches to `apcs_migrator`; hand-written DDL is refused).
  `scripts/e2e/` starts a database of its own with Docker. Deployment, environments, and backups
  are described in [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md).
- **The app-wide rate limiter is off in `Development`** (`appsettings.Development.json`), so local
  work is never throttled. Set `RateLimiting__Enabled=true` to try it; the limits are the
  `RateLimiting:*` keys read by `API/Extensions/ApiRateLimiting.cs`.
- The reports of the end-to-end suite live in the `test_report_runs` table, so a run outlives the
  machine that made it. Integration fixtures build the schema with `EnsureCreated`, so the table
  exists wherever tests run; against a pre-provisioned database, `APCS_TEST_CONNECTION_STRING`
  points at a database prepared by the fixture or `scripts/e2e/bootstrap-test-db.sh`.

## Changing the database schema

The shared dev database refuses DDL from a `dev_<name>` session unless it comes from
`scripts/Migrate-DevDb.ps1`, so a schema change made any other way fails. Follow this sequence for every table, column, index, constraint, view, function, or
catalog-data change:

1. `./scripts/New-Migration.ps1 -Name <what_it_does>` creates an empty
   `migrations/V<yyyyMMddHHmm>__<what_it_does>.sql`; write the SQL there.
2. `./scripts/Migrate-DevDb.ps1 -Scaffold` applies it to the dev database through Flyway (Docker
   required) and regenerates `Domain/Entities/Generated` and `Infrastructure/Persistence/Generated`.
3. Commit the migration and the regenerated files in the same change as the code that uses them.

Never:

- run DDL directly with `psql`, DBeaver, a database MCP tool, or a one-off script, and never put
  schema changes in `scripts/sql/` instead of `migrations/`;
- run `SET ROLE apcs_migrator` by hand: it bypasses `flyway_schema_history`, so the change never
  reaches production;
- edit a migration that has already been applied to the dev database (write a new one), or use EF
  migrations or `dotnet ef database update`;
- hand-edit files under the two `Generated` folders.

Reading the errors:

| Error | Meaning | Fix |
|---|---|---|
| `Schema changes on the shared dev database must go through Flyway migrations`, `must be owner of table ...`, or `permission denied for schema public` | DDL ran outside `Migrate-DevDb.ps1` | Move the SQL into a migration and run step 2 |
| `role "apcs_migrator" does not exist` | `ConnectionStrings__DefaultConnection` in `.env` is not the shared dev database (for example the old Neon URL) | Use the personal `dev_<name>` connection string |
| `relation ... does not exist` / `column ... does not exist` while running the API | The dev database lacks a migration from the current branch | `./scripts/Migrate-DevDb.ps1 -Command info`, then run it without `-Command` |
| `Migration checksum mismatch for migration version ...` | An applied migration file was edited | Restore the file or, if the edit is intended, `./scripts/Migrate-DevDb.ps1 -Command repair` |

Details and the reasoning live in [`migrations/README.md`](migrations/README.md).

## Before changing files

- Inspect `git status` and preserve unrelated or user-owned changes.
- Read the canonical documents named by the task table above.
- Inspect the target code and the closest existing implementation.
- Confirm actual project references, namespaces, and test framework instead of importing generic .NET patterns.
- Make the smallest coherent change that satisfies the request.

## While working

- Do not introduce unrequested technologies, abstractions, or broad refactors.
- Do not expose or commit secrets, tokens, passwords, connection strings, or private keys.
- Keep production behavior changes out of documentation-only tasks.
- Pass cancellation through asynchronous call chains and preserve established error and HTTP behavior when code changes are authorized.
- Use the APCS-specific tools under `.agent/` only when their trigger matches the task.

## Verification and handoff

- Run verification proportional to the change; use `dotnet test Capstone.sln` for executable changes.
- Use `./scripts/Test-WithCoverage.ps1` when coverage-sensitive behavior or CI configuration changes.
- Review the final diff for accidental edits, stale paths, secrets, and architecture violations.
- Summarize the outcome, verification performed, and any known gap or follow-up. Do not claim checks that were not run.

Update canonical documentation only for durable behavior or policy changes. Do not create scan-dated inventories that duplicate facts readily derived from the repository.
