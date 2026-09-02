# Contributing to the APCS Backend

This guide defines the development workflow and coding conventions. Architecture decisions belong in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and test strategy belongs in [docs/TESTING.md](docs/TESTING.md).

## Prepare the change

1. Inspect `git status` and preserve unrelated work.
2. Read the relevant canonical documentation.
3. Inspect the target code and a similar existing feature. Authentication under `Application/Features/Auth/` is the current structural example.
4. Clarify expected behavior, authorization, ownership, failures, and persistence before editing.
5. Keep one change focused on one coherent outcome.

## Coding conventions

- Use the `APCS.Api`, `APCS.Application`, `APCS.Domain`, `APCS.Infrastructure`, and `APCS.Common` root namespaces.
- Use file-scoped namespaces and nullable reference types, matching the current projects.
- Name use-case types `{UseCase}Command`, `{UseCase}Query`, `{UseCase}CommandHandler`, `{UseCase}QueryHandler`, `{UseCase}Validator`, and `{UseCase}Response`.
- Keep use-case identifiers such as `UC01` in requirements, documentation, or remarks—not folder, namespace, or type names.
- Suffix asynchronous methods with `Async` unless implementing a framework contract such as MediatR's `Handle`.
- Pass the supplied `CancellationToken` through every asynchronous dependency.
- Prefer `DateTimeOffset` for persisted timestamps and use an `Utc` suffix for UTC properties.
- Prefer sealed classes and records when inheritance is not intended. Use primary constructors when they make dependency injection clearer, not as a mandatory rewrite rule.
- Add XML documentation to public contracts and domain APIs where it clarifies intent; avoid comments that merely repeat the identifier.
- Use `Result`/`Result<T>` for expected failures and allow unexpected exceptions to reach the API exception middleware.

## Adding a new feature

1. **Identify the feature.** Reuse an existing feature area or create `Application/Features/{Feature}`.
2. **Choose Command or Query.** A Command changes state; a Query reads without changing state.
3. **Create the use-case folder.** Use `Commands/{UseCase}` or `Queries/{UseCase}` beneath the feature.
4. **Define the Application contract.** Add the command/query, response, handler, and a FluentValidation validator when the input has validation rules.
5. **Place related types deliberately.** Keep a response in its use-case folder. Put a model reused by multiple use cases under `Application/Features/{Feature}/Common`. Put external contracts under `Application/Abstractions/{Concern}`.
6. **Change Domain only for domain needs.** Add behavior, invariants, value objects, or entities when the business model requires them; do not create an aggregate or repository merely because a table exists.
7. **Choose persistence access.** Commands that persist an aggregate or lifecycle use a focused repository plus `IUnitOfWork`. Database-backed queries use `IReadDbContext` and project to response models. Do not create a generic repository or one repository per table.
8. **Implement adapters in Infrastructure.** Add EF configurations, repositories, service adapters, options, and DI registration there. Generate a migration only when the schema changes.
9. **Add the API endpoint.** Bind and dispatch the Application command/query directly when its contract exactly matches the HTTP body. Introduce an API request model only when the HTTP contract differs or needs transport-specific mapping. Keep cookies, status codes, authentication, authorization, and `ProblemDetails` in API; do not inject Infrastructure into controllers.
10. **Add tests.** Mirror the production namespace in the appropriate unit-test project. Add integration coverage when behavior depends on PostgreSQL, Redis, migrations, authorization, DI wiring, or the complete HTTP pipeline.
11. **Verify and review.** Build, test, inspect the diff, and update documentation only when a durable contract or rule changed.

## API and security checklist

- Use `[Authorize]` for protected endpoints and `[AllowAnonymous]` only for intentionally public endpoints.
- Validate ownership for seller-scoped resources through the authenticated-user abstraction.
- Do not return EF or Identity entities from endpoints; map them to response models.
- Do not log passwords, access tokens, refresh tokens, API keys, signing keys, or encrypted secret values.
- Keep raw refresh tokens outside persistence and expose them only through the intended secure cookie flow.
- Let API code own cookie options and HTTP response mapping.

## EF Core migrations

Create a migration from the solution root:

```powershell
dotnet ef migrations add <MigrationName> --project Infrastructure --startup-project API
```

Review the generated migration, model snapshot, destructive operations, defaults, constraints, and provider-specific SQL before applying it:

```powershell
dotnet ef database update --project Infrastructure --startup-project API
```

Do not edit or regenerate unrelated migrations as part of another feature.

## Pull requests

The repository currently enforces pull-request and `main`-branch builds through GitHub Actions. It does not define a special branch-naming or commit-message convention.

A pull request should explain the changed behavior, important architecture decisions, verification performed, and any intentionally deferred tests or follow-up work.

## Definition of done

- The change follows the documented dependency and placement rules, or a current deviation is explicitly identified.
- Controllers remain focused on HTTP concerns.
- Expected failures use the established result/error flow.
- Input validation and authorization are present where required.
- New behavior has success, expected-failure, boundary, and regression coverage as applicable.
- Cancellation reaches asynchronous dependencies.
- No secret or sensitive value is exposed.
- `dotnet test Capstone.sln` passes.
- Coverage-sensitive changes pass `./scripts/Test-WithCoverage.ps1`.
- Documentation and AI tooling link to canonical rules rather than copying them.
