# APCS Backend Architecture

This document is the source of truth for architecture, the Service/Repository pattern, dependency direction, and type placement. It describes both the intended rules for new work and the current implementation where it differs.

## Technology baseline

- .NET 8 / ASP.NET Core controller-based Web API.
- Feature services (`I{Feature}Service`) for use cases, backed by a generic `IRepository<T>` plus focused repositories.
- FluentValidation for request validation, invoked directly by the service method it guards.
- Entity Framework Core 8 with Npgsql and a database-first PostgreSQL model reverse-engineered from Neon schema `public`.
- Direct EF account persistence with GUID `User` identifiers, ASP.NET Core's `PasswordHasher<User>`, role codes, and a permission catalogue; ASP.NET Identity stores are not used.
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

Domain owns business state and behavior: entities, aggregate roots, value objects, invariants, domain exceptions, and future domain events. The database-first POCOs under `Domain/Entities/Generated` are a deliberate persistence-shaped boundary; custom behavior belongs in partial files outside that generated directory. HTTP concerns, identity framework types, cache clients, and external SDKs do not belong here.

An entity is not automatically an aggregate root, and a database table does not automatically require a repository. Model the consistency boundary and behavior required by a use case.

### Application

Application owns use cases and external contracts. It coordinates Domain behavior, authorizes application-level operations, validates input, and returns `Result`/`Result<T>` outcomes.

Use cases for one feature are grouped behind a single feature service and organized as:

```text
Application/Features/{Feature}/
├── I{Feature}Service.cs                # one method per use case
├── {Feature}Service.cs                 # implementation
├── Dtos/
│   ├── Request/
│   │   └── {UseCase}RequestDto.cs      # when the use case takes input beyond primitives
│   └── Response/
│       └── {UseCase}ResponseDto.cs     # when the use case returns data
├── Validators/
│   └── {UseCase}Validator.cs           # when input rules exist; validated by the service method itself
└── Common/                             # models shared inside this feature
```

A use case that changes state persists through a focused repository (or `IRepository<T>` for common CRUD) plus `IUnitOfWork`. A use case that only reads projects its response directly from `IRepository<T>.Query()` or a narrower Application abstraction. The current example is `APCS.Application.Features.Auth`: `IAuthService`/`AuthService` implement `RegisterAsync`, `LoginAsync`, `LogoutAsync`, `RefreshTokenAsync`, `VerifyEmailAsync`, `ResendVerificationEmailAsync`, and `GetCurrentUserAsync`. UC identifiers remain traceability metadata only; they are not part of folder, namespace, or type names.

### Infrastructure

Infrastructure owns the generated EF Core `AppDbContext`, custom partial mappings, focused repository implementations, the direct account adapter, JWT generation, Redis, email, storage, messaging, and external API adapters. It does not own or apply migrations: Neon schema `public` is the persistence source of truth.

Register implementations in `Infrastructure/DependencyInjection.cs`. Infrastructure types must not leak into Domain or API response contracts.

### API

API owns controllers, any HTTP-specific transport models, authentication/authorization middleware, cookies, status codes, `ProblemDetails`, CORS, Swagger, and host composition.

Controllers may bind an Application request directly and pass it to the feature service when the HTTP payload has exactly the same contract and contains no HTTP-only data. Create an API transport model only when the public HTTP contract differs, requires transformation or versioning, combines multiple inputs, or contains transport-specific fields. Controllers handle HTTP-only side effects and map results, but must not contain domain decisions or inject `AppDbContext` or Infrastructure services.

### Common

Common currently owns `Result`, `Error`, `ErrorType`, paging, configuration helpers, shared constants, and general helpers. Keep this layer small. Feature-specific errors, DTOs, and business rules belong with their feature or Domain.

## Service and repository decisions

| Situation | Use | Do not use |
|---|---|---|
| Operation changes business state | Service method, `IRepository<T>` and/or a focused repository, `IUnitOfWork` | Injecting `AppDbContext` directly into a service |
| Operation reads PostgreSQL data | Service method, `IRepository<T>.Query()` (or a focused repository query), LINQ filtering and direct response projection | Tracking entities for read-only output, `AppDbContext` injection, entity return from API |
| Operation uses accounts/JWT/cache/email/external API | Application abstraction grouped by concern | Direct Infrastructure implementation in Application or API |
| Several writes must be atomic | `IUnitOfWorkTransaction` where the use case truly requires a transaction | A transaction around every service method by default |

`IRepository<TEntity>` is the generic persistence contract every focused repository builds on: `GetByIdAsync`, `GetAllAsync`, `FindAsync`, `Query()` (a no-tracking `IQueryable` root), and `AddAsync`/`UpdateAsync`/`RemoveAsync`. The three write methods take an optional `saveChange` flag (default `false`) so a service can either stage several changes and commit them together through `IUnitOfWork.SaveChangesAsync`, or opt into an immediate save for a single, self-contained write. Service methods must filter and project `Query()` results before materializing data, and must not expose the query root or return a Domain/EF entity as the response.

`Query()` always runs against the database, so it does not see writes staged but not yet saved. A method that writes and then reads the same data back must save (or use `saveChange: true`) in between.

Create a focused repository, on top of `IRepository<T>`, only when an operation needs more than common CRUD — a consistency boundary, a lifecycle query, or a lookup shape `Query()` cannot express cleanly. `IAuthTokenRepository` is the current example: it adds `GetByHashAsync` and `GetRedeemableAsync` on top of the inherited `IRepository<AuthToken>` members.

### Database-first schema workflow

Neon schema `public` is authoritative. Generated POCOs live in `Domain/Entities/Generated`, and the generated context lives in `Infrastructure/Persistence/Generated`. Regenerate both with `./scripts/Scaffold-Database.ps1` after an approved database change. The script reads its named connection from configuration, stages the scaffold, and replaces only those two generated directories. Never put custom behavior in them, add an EF migration, or run `dotnet ef database update` for this repository.

## Type placement

| Type | Location |
|---|---|
| HTTP body identical to one Application request | Bind the request/response directly in the controller |
| HTTP contract differing from the Application request | API, co-located with its controller, or `API/Contracts/{Feature}/` when shared |
| Feature service interface/implementation | `Application/Features/{Feature}/` |
| Request DTO, response DTO, validator | `Application/Features/{Feature}/Dtos/Request/`, `Dtos/Response/`, and `Validators/` |
| Model shared across use cases in one feature | `Application/Features/{Feature}/Common/` |
| Account, cache, email, persistence, or external-service contract | `Application/Abstractions/{Concern}/` |
| Contract-specific DTO | `Application/Abstractions/{Concern}/Dtos/` |
| Generated database POCO | `Domain/Entities/Generated/` |
| Entity partial behavior, value object, invariant, domain exception | Domain outside generated directories |
| Generated EF context, custom partial mapping, repository implementation, provider options | Infrastructure |
| HTTP result mapping, cookie, authorization, middleware | API |

## Validation, errors, and HTTP mapping

- FluentValidation validators live in the feature's `Validators/` folder and are discovered by `AddApplication()` for DI, but are not run automatically: each service method that needs one takes the matching `IValidator<TRequest>` as a dependency and calls `ValidateAsync` itself at the top of the method.
- `ValidationResultExtensions.ToValidationError()` (`Application/Common/Validation/`) converts a failed `ValidationResult` into the established `Error.Validation` shape; service methods should not hand-roll this conversion or repeat syntactic validation.
- Expected failures return `Result.Failure` with a stable error code and appropriate `ErrorType`.
- Unexpected exceptions bubble to `GlobalExceptionMiddleware`.
- API `ResultExtensions` maps error types to `ProblemDetails` and HTTP status codes.
- Domain methods may reject invalid domain state with a domain exception; do not use exceptions as ordinary application branching.

## Naming and examples

- Namespaces follow their project and folders, for example `APCS.Application.Features.Auth`.
- Use `I{Feature}Service`/`{Feature}Service` for a feature's use cases, with one `{UseCase}Async` method per use case.
- Use `{UseCase}RequestDto` (under `Dtos/Request/`) for input beyond primitives and `{UseCase}ResponseDto` (under `Dtos/Response/`) for a use-case result; use feature `Common` only when reuse across use cases is real.
- Use `*Repository` only for focused persistence contracts beyond `IRepository<T>`, and `*Service` for feature use cases and external capability abstractions alike.
- Use `Async` on asynchronous service/repository methods and `*AtUtc` on UTC timestamps.

Good:

```csharp
public sealed class ProductService(
    IProductRepository products,
    IUnitOfWork unitOfWork)
    : IProductService
{
    // Apply domain behavior, add through the focused repository, then commit once.
}
```

Bad:

```csharp
public sealed class ProductService(
    AppDbContext dbContext)
    : IProductService
{
    // A service should depend on IRepository<T>/a focused repository, not AppDbContext directly.
}
```

Good query shape:

```csharp
var response = await products.Query()
    .Where(product => product.Id == request.Id)
    .Select(product => new GetProductResponse(product.Id, product.Name))
    .SingleOrDefaultAsync(cancellationToken);
```

Bad API shape:

```csharp
public Task<Product> Get(AppDbContext dbContext, Guid id) =>
    dbContext.Products.SingleAsync(product => product.Id == id);
```

The examples show dependency and projection shape; they do not claim that the illustrative product use cases already exist.

## Current implementation deviations and gaps

These facts are documented so new work does not mistake a target rule for an already-complete implementation:

- `Application/Application.csproj` references Entity Framework Core. `AuthService.RefreshTokenAsync` and `AuthService.LogoutAsync` catch `DbUpdateConcurrencyException` directly; provider failure translation is not yet fully abstracted.
- `AccountService` uses `IAccountRepository.Query()` (inherited from `IRepository<User>`) for its email lookups; other read paths (roles, current user) still go through `IAccountService` rather than a direct query in a handler.
- Generated entities intentionally have scaffolded public setters and navigation properties. Custom behavior currently exists for `AuthToken` and `User`; generated accessors and navigations are not treated as behavioral APIs.
- The `permissions` and `role_permissions` tables are mapped but no authorization path consults them yet; active roles alone drive `[Authorize]`. `AccountService` creates the default Seller role lazily when it is missing, and there is no administrator bootstrap.
- The Neon auth schema has no lockout fields or refresh-token lineage. Administrative account states still make an account inactive, but automatic lockout and descendant-token revocation on replay are not available.
- `AggregateRoot` is presently a marker and domain events are not implemented.
- `EmailService` logs a send request and completes without delivering email.
- Redis is configured and unit-tested, but the repository has no Redis integration tests.
- Model tests verify the reverse-engineered table count and `xmin` concurrency metadata, but no integration-test project currently verifies PostgreSQL writes, provider query behavior, authorization, or the complete HTTP pipeline.

These are not instructions to refactor unrelated code. Address a deviation only through an approved feature or architectural change.

## Where to go next

- Deliver a change against these rules through *Adding a new feature* in [`../CONTRIBUTING.md`](../CONTRIBUTING.md).
- Choose the test level for that change in [`TESTING.md`](TESTING.md).
- Add an entry to the deviations section above only when an approved change leaves one, and delete the entry in the same change that closes it.
