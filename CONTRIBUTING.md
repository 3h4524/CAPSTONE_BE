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
- Name feature types `I{Feature}Service`, `{Feature}Service`, `{UseCase}RequestDto`, `{UseCase}ResponseDto`, and `{UseCase}Validator`.
- Keep use-case identifiers such as `UC01` in requirements, documentation, or remarks—not folder, namespace, or type names.
- Suffix asynchronous methods with `Async` unless implementing a framework contract that names its own method.
- Pass the supplied `CancellationToken` through every asynchronous dependency.
- Prefer `DateTimeOffset` for application/domain timestamps and use an `Utc` suffix for UTC properties. Database-first generated POCOs keep the `DateTime` types and names inferred from PostgreSQL; convert application timestamps with `UtcDateTime` at that boundary.
- Prefer sealed classes and records when inheritance is not intended. Use primary constructors when they make dependency injection clearer, not as a mandatory rewrite rule.
- Add XML documentation to public contracts and domain APIs where it clarifies intent; avoid comments that merely repeat the identifier.
- Use `Result`/`Result<T>` for expected failures and allow unexpected exceptions to reach the API exception middleware.

## Adding a new feature

1. **Identify the feature.** Reuse an existing feature area or create `Application/Features/{Feature}` with `I{Feature}Service`/`{Feature}Service`.
2. **Add a use case as a service method.** Add one `{UseCase}Async` method to the feature's service interface and implementation; a use case that changes state and one that only reads are both just methods on the same service.
3. **Define the Application contract.** Add a `{UseCase}RequestDto` under `Dtos/Request/` when the use case takes input beyond primitives, a `{UseCase}ResponseDto` under `Dtos/Response/` when it returns data, and a `{UseCase}Validator` under `Validators/` when the input has validation rules; call the validator explicitly at the top of the service method.
4. **Place related types deliberately.** Keep request DTOs under the feature's `Dtos/Request/` and response DTOs under `Dtos/Response/`. Put a model reused by multiple use cases under `Application/Features/{Feature}/Common`. Put external contracts under `Application/Abstractions/{Concern}`.
5. **Change Domain only for domain needs.** Add behavior, invariants, value objects, or entities when the business model requires them; do not create an aggregate or repository merely because a table exists.
6. **Choose persistence access.** Use `IRepository<T>` for common CRUD; add a focused repository only when a use case needs a lifecycle query or lookup shape `IRepository<T>.Query()` can't express cleanly. Pass `saveChange: true` on a repository write for a single, self-contained change, or leave the default `false` and commit through `IUnitOfWork.SaveChangesAsync` when several writes belong to one use case.
7. **Implement adapters in Infrastructure.** Add custom partial mappings, repositories, service adapters, options, and DI registration there. When an approved change alters PostgreSQL, change Neon first and run the database-first scaffold script; do not generate a migration.
8. **Add the API endpoint.** Bind the Application request directly in the controller and pass it to the feature service when its contract exactly matches the HTTP body. Introduce an API request model only when the HTTP contract differs or needs transport-specific mapping. Keep cookies, status codes, authentication, authorization, and `ProblemDetails` in API; do not inject Infrastructure into controllers.
9. **Add tests.** Mirror the production namespace in the appropriate unit-test project. Add integration coverage when behavior depends on PostgreSQL, Redis, reverse-engineered mappings, authorization, DI wiring, or the complete HTTP pipeline.
10. **Verify and review.** Build, test, inspect the diff, and update documentation only when a durable contract or rule changed.

## API and security checklist

- Use `[Authorize]` for protected endpoints and `[AllowAnonymous]` only for intentionally public endpoints.
- Validate ownership for user-scoped resources through the authenticated-user abstraction.
- Do not return EF entities from endpoints; map them to response models.
- Do not log passwords, access tokens, refresh tokens, API keys, signing keys, or encrypted secret values.
- Keep raw refresh tokens outside persistence and expose them only through the intended secure cookie flow.
- Let API code own cookie options and HTTP response mapping.

## Database-first schema workflow

Neon schema `public` is the source of truth. Make an approved schema change in Neon first, set `ConnectionStrings__DefaultConnection` in the ignored local `.env`, then run from the solution root:

```powershell
./scripts/Scaffold-Database.ps1
```

The script reads the named connection from configuration and replaces only:

- `Domain/Entities/Generated`
- `Infrastructure/Persistence/Generated`

Review the generated entity/context diff, rebuild, and run the full test suite. Put all custom entity behavior and context configuration in partial files outside the generated directories so re-scaffolding cannot erase it. Do not add EF migrations or run `dotnet ef database update` in this repository.

## Pull requests

The repository currently enforces pull-request and `main`-branch builds through GitHub Actions. It does not define a special branch-naming or commit-message convention.

A pull request should explain the changed behavior, important architecture decisions, verification performed, and any intentionally deferred tests or follow-up work. GitHub loads [`.github/pull_request_template.md`](.github/pull_request_template.md) into the description; complete its checklist instead of replacing it.

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
