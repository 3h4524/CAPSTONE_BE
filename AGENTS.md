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
| Change an entity or the schema | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for Domain and persistence rules, then *Database-first schema workflow* in [`CONTRIBUTING.md`](CONTRIBUTING.md) |
| Prepare a pull request | *Definition of done* in [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`.github/pull_request_template.md`](.github/pull_request_template.md) |
| Use an APCS prompt or workflow | [`.agent/README.md`](.agent/README.md) |

A change that matches more than one row reads every row that applies.

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
