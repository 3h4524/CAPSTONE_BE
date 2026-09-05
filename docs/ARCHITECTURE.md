# APCS Backend Architecture

This document is the source of truth for architecture, CQRS, dependency direction, and type placement. It describes both the intended rules for new work and the current implementation where it differs.

## Technology baseline

- .NET 8 / ASP.NET Core controller-based Web API.
- MediatR for commands, queries, handlers, and pipeline behaviours.
- FluentValidation for request validation.
- Entity Framework Core 8 with PostgreSQL and snake-case database naming.
- ASP.NET Core Identity with integer `Seller` identifiers.
- Custom JWT access tokens and hashed, rotating refresh tokens.
- Redis through `IDistributedCache` and the Application `ICacheService` abstraction.
- MSTest, FluentAssertions, and Moq for the current unit-test suite.

Do not add a technology merely because generic Clean Architecture or .NET guidance recommends it. Introduce a dependency only for an approved feature and document the durable architectural consequence.

## Dependency direction

The current project references are:

```text
APCS.Api ───────────────► APCS.Application
   │                     APCS.Infrastructure
   └────────────────────► APCS.Common

APCS.Infrastructure ───► APCS.Application
                         APCS.Domain
                         APCS.Common

APCS.Application ──────► APCS.Domain
                         APCS.Common

APCS.Domain ───────────► APCS.Common
```

Rules for new work:

- Domain must not reference Application, Infrastructure, or API.
- Application must not reference Infrastructure or API.
- Infrastructure implements abstractions owned by Application and maps Domain objects to external systems.
- API is the composition root and owns HTTP pipeline behavior.
- Common must contain only broadly reusable primitives and utilities, never feature orchestration or mutable business workflows.

Package references are dependencies too. If Application must recognize a provider-specific failure, prefer translating it behind an Application abstraction rather than leaking the provider exception through a handler.

## Layer responsibilities

### Domain

Domain owns business state and behavior: entities, aggregate roots, value objects, invariants, domain exceptions, and future domain events. Persistence mappings, HTTP concerns, identity framework types, cache clients, and external SDKs do not belong here.

An entity is not automatically an aggregate root, and a database table does not automatically require a repository. Model the consistency boundary and behavior required by a use case.

### Application

Application owns use cases and external contracts. It coordinates Domain behavior, authorizes application-level operations, validates input through the pipeline, and returns `Result`/`Result<T>` outcomes.

Use cases are organized as:

```text
Application/Features/{Feature}/
├── Commands/
│   └── {UseCase}/
│       ├── {UseCase}Command.cs
│       ├── {UseCase}CommandHandler.cs
│       ├── {UseCase}Validator.cs       # when input rules exist
│       └── {UseCase}Response.cs        # when the use case returns data
├── Queries/
│   └── {UseCase}/
│       ├── {UseCase}Query.cs
│       ├── {UseCase}QueryHandler.cs
│       ├── {UseCase}Validator.cs       # when input rules exist
│       └── {UseCase}Response.cs
└── Common/                             # models shared inside this feature
```

The current example is `APCS.Application.Features.Auth`, including `Commands.Register` and `Queries.GetCurrentUser`. UC identifiers remain traceability metadata only; they are not part of folder, namespace, or type names.

### Infrastructure

Infrastructure owns EF Core `DbContext`, entity configurations, migrations, focused repository implementations, Identity types and stores, JWT generation, Redis, email, storage, messaging, and external API adapters.

Register implementations in `Infrastructure/DependencyInjection.cs`. Infrastructure types must not leak into Domain or API response contracts.

### API

API owns controllers, any HTTP-specific transport models, authentication/authorization middleware, cookies, status codes, `ProblemDetails`, CORS, Swagger, and host composition.

Controllers may bind and dispatch an Application command/query directly when the HTTP payload has exactly the same contract and contains no HTTP-only data. Create an API transport model only when the public HTTP contract differs, requires transformation or versioning, combines multiple inputs, or contains transport-specific fields. Controllers handle HTTP-only side effects and map results, but must not contain domain decisions or inject `AppDbContext` or Infrastructure services.

### Common

Common currently owns `Result`, `Error`, `ErrorType`, paging, configuration helpers, shared constants, and general helpers. Keep this layer small. Feature-specific errors, DTOs, and business rules belong with their feature or Domain.

## CQRS and persistence decisions

| Situation | Use | Do not use |
|---|---|---|
| Operation changes business state | Command handler, focused aggregate/lifecycle repository, `IUnitOfWork` | `IReadDbContext`, generic repository, repository per table |
| Operation reads PostgreSQL data | Query handler, `IReadDbContext`, LINQ filtering and direct response projection | Tracking entities for read-only output, `AppDbContext` injection, entity return from API |
| Operation uses Identity/JWT/cache/email/external API | Application abstraction grouped by concern | Direct Infrastructure implementation in Application or API |
| Several writes must be atomic | `IUnitOfWorkTransaction` where the use case truly requires a transaction | A transaction around every handler by default |

`IReadDbContext` exposes no-tracking `IQueryable` roots. Query handlers must filter and project before materializing data. Do not expose the query root beyond the handler or return a Domain/EF entity as the response.

Create a command repository only for operations needed by a consistency boundary or lifecycle. `IRefreshTokenRepository` is the current example: it finds a token by hash and adds a new token while `IUnitOfWork` commits the lifecycle change.

## Type placement

| Type | Location |
|---|---|
| HTTP body identical to one Application request | Bind the command/query directly in the controller |
| HTTP contract differing from the Application request | API, co-located with its controller, or `API/Contracts/{Feature}/` when shared |
| Command/query, handler, validator, response | Its Application use-case folder |
| Model shared across use cases in one feature | `Application/Features/{Feature}/Common/` |
| Identity, cache, email, persistence, or external-service contract | `Application/Abstractions/{Concern}/` |
| Contract-specific model | `Application/Abstractions/{Concern}/Models/` |
| Entity, value object, invariant, domain exception | Domain |
| EF mapping, migration, repository implementation, provider options | Infrastructure |
| HTTP result mapping, cookie, authorization, middleware | API |

## Validation, errors, and HTTP mapping

- FluentValidation validators live beside the command or query and are discovered by `AddApplication()`.
- `ValidationBehaviour` converts validation failures into the established result shape; handlers should not repeat syntactic validation.
- Expected failures return `Result.Failure` with a stable error code and appropriate `ErrorType`.
- Unexpected exceptions bubble to `GlobalExceptionMiddleware`.
- API `ResultExtensions` maps error types to `ProblemDetails` and HTTP status codes.
- Domain methods may reject invalid domain state with a domain exception; do not use exceptions as ordinary application branching.

## Naming and examples

- Namespaces follow their project and folders, for example `APCS.Application.Features.Auth.Commands.Register`.
- Use `{UseCase}Command` for writes and `{UseCase}Query` for reads.
- Use `{UseCase}Response` for a use-case result and feature `Common` only when reuse is real.
- Use `*Repository` only for focused persistence contracts and `*Service` for external capability abstractions.
- Use `Async` on asynchronous service/repository methods and `*AtUtc` on UTC timestamps.

Good:

```csharp
public sealed class CreateProductCommandHandler(
    IProductRepository products,
    IUnitOfWork unitOfWork)
{
    // Apply domain behavior, add through the focused repository, then commit once.
}
```

Bad:

```csharp
public sealed class CreateProductCommandHandler(
    IGenericRepository<Product> products,
    IReadDbContext readDbContext)
{
    // A command should not introduce a generic repository or use the read model to write.
}
```

Good query shape:

```csharp
var response = await readDbContext.Products
    .Where(product => product.Id == request.Id)
    .Select(product => new GetProductResponse(product.Id, product.Name))
    .SingleOrDefaultAsync(cancellationToken);
```

Bad API shape:

```csharp
public Task<Product> Get(AppDbContext dbContext, int id) =>
    dbContext.Products.SingleAsync(product => product.Id == id);
```

The examples show dependency and projection shape; they do not claim that the illustrative product use cases already exist.

## Current implementation deviations and gaps

These facts are documented so new work does not mistake a target rule for an already-complete implementation:

- `Application/Application.csproj` references Entity Framework Core. `RefreshTokenCommandHandler` catches `DbUpdateConcurrencyException` directly; provider failure translation is not yet fully abstracted.
- `IReadDbContext` is implemented by `AppDbContext` and exposes no-tracking roots, but no production query currently demonstrates database projection. `GetCurrentUserQueryHandler` reads through `IIdentityService`.
- The schema model contains many entities with private setters, but most do not yet expose construction or business-transition methods. `Email`, `RefreshToken`, and soft-delete behavior are the clearest current domain-behavior examples.
- `AggregateRoot` is presently a marker and domain events are not implemented.
- `EmailService` logs a send request and completes without delivering email.
- Redis is configured and unit-tested, but the repository has no Redis integration tests.
- No integration-test project currently verifies PostgreSQL mappings, migrations, provider queries, authorization, or the complete HTTP pipeline.

These are not instructions to refactor unrelated code. Address a deviation only through an approved feature or architectural change.

## Where to go next

- Deliver a change against these rules through *Adding a new feature* in [`../CONTRIBUTING.md`](../CONTRIBUTING.md).
- Choose the test level for that change in [`TESTING.md`](TESTING.md).
- Add an entry to the deviations section above only when an approved change leaves one, and delete the entry in the same change that closes it.
