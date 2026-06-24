# Current Backend State

> Last scanned: 2026-06-24

## Solution

- **File**: `Capstone.sln`
- **Target**: .NET 8.0
- **Root namespace prefix**: `APCS`

## Projects

| Project | Assembly | Role |
|---------|----------|------|
| API | APCS.Api | ASP.NET Core host, controllers, middleware |
| Application | APCS.Application | Use cases, MediatR handlers, validators |
| Domain | APCS.Domain | Entities, value objects, enums, exceptions |
| Infrastructure | APCS.Infrastructure | EF Core, Identity, JWT, external services |
| Common | APCS.Common | Result pattern, Error types, constants, helpers |

## Implemented Features

- ✅ JWT authentication (access token + refresh token rotation)
- ✅ User registration with ASP.NET Core Identity
- ✅ Login with credential validation and lockout
- ✅ Refresh token with rotation and revocation
- ✅ Logout with token revocation
- ✅ Get current user endpoint
- ✅ Global exception handling (ProblemDetails)
- ✅ MediatR pipeline with FluentValidation
- ✅ Logging pipeline behaviour
- ✅ CORS configuration
- ✅ Swagger/OpenAPI with JWT bearer support
- ✅ Health check endpoint (`/health`)
- ✅ Result/Error pattern (`Result<T>`, `Error`, `ErrorType`)
- ✅ `.env` file loader for local development

## Auth/JWT

- **Controller**: `API/Controllers/AuthController.cs`
- **JWT config**: `Infrastructure/Options/JwtOptions.cs` — bound from `Jwt` config section
- **Token generation**: `Infrastructure/Services/JwtService.cs` — HMAC-SHA256 signing
- **Identity service**: `Infrastructure/Services/IdentityService.cs` — wraps ASP.NET Core Identity
- **Current user**: `Infrastructure/Services/CurrentUserService.cs` — reads claims from HttpContext
- **Refresh token entity**: `Domain/Entities/RefreshToken.cs` — hashed storage, rotation, revocation
- **Identity user**: `Infrastructure/Persistence/ApplicationUser.cs` — extends `IdentityUser<Guid>`

### Auth Use Cases

| Use Case | Folder | Type |
|----------|--------|------|
| Register | `Application/UseCases/Auth/UC01_Register/` | Command |
| Login | `Application/UseCases/Auth/UC02_Login/` | Command |
| Refresh Token | `Application/UseCases/Auth/UC02b_RefreshToken/` | Command |
| Logout | `Application/UseCases/Auth/UC02c_Logout/` | Command |
| Get Current User | `Application/UseCases/Auth/UC02d_GetCurrentUser/` | Query |

### Auth Interfaces (Application layer)

- `IIdentityService` — user CRUD, credential validation, role management
- `IJwtService` — access token generation, refresh token generation/hashing
- `ICurrentUser` — current user ID, email, authentication status
- `IEmailService` — email sending (interface defined, basic implementation)
- `IAppDbContext` — EF Core DbContext abstraction
- `IAppDbTransaction` — transaction abstraction

## Database

- **Provider**: PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`
- **DbContext**: `Infrastructure/Persistence/AppDbContext.cs`
- **Base**: `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`
- **Naming**: Snake-case convention (`EFCore.NamingConventions`)
- **Migrations**: 2 migrations applied (InitialIdentityAuth, AddRefreshTokenConcurrency)
- **Connection string**: from `ConnectionStrings:DefaultConnection` (loaded via `.env`)
- **Tables**: `users`, `roles`, `user_roles`, `user_claims`, `user_logins`, `role_claims`, `user_tokens`, `refresh_tokens`

### Entity Configurations

- `Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- `Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`

## API Layer

- **Style**: Controller-based (not Minimal APIs)
- **Controllers**: `AuthController` only
- **Middleware**: `GlobalExceptionMiddleware` — catches unhandled exceptions → ProblemDetails
- **Extensions**: `ResultExtensions` — maps `Result<T>` to HTTP responses, `DotEnvLoader`
- **Auth**: JWT Bearer via `Microsoft.AspNetCore.Authentication.JwtBearer`
- **Swagger**: Swashbuckle with Bearer scheme configured

## Application Layer

- **Pattern**: MediatR CQRS — Command/Query → Handler
- **Validation**: `FluentValidation` via `ValidationBehaviour` pipeline
- **Logging**: `LoggingBehaviour` pipeline
- **DI**: `AddApplication()` — registers MediatR, validators, pipeline behaviours
- **Conventions**: One folder per use case, each containing Command/Query, Handler, Validator, Response

## Domain Layer

- **Entities**: `User`, `RefreshToken`
- **Value Objects**: `Email`
- **Enums**: `UserRole`
- **Base classes**: `BaseEntity` (Guid Id), `AggregateRoot`
- **Exceptions**: `DomainException`
- **Dependencies**: Only `Common` project

## Infrastructure Layer

- **Persistence**: `AppDbContext`, `ApplicationUser`, entity configurations, migrations
- **Services**: `JwtService`, `IdentityService`, `CurrentUserService`, `EmailService`
- **Options**: `JwtOptions` (strongly-typed, validated on start)
- **DI**: `AddInfrastructure(IConfiguration)` — registers DbContext, Identity, JWT, services

## Common Layer

- **Models**: `Result`, `Result<T>`, `Error`, `ErrorType`, `PagedResult<T>`
- **Wrappers**: `ApiResponse`
- **Constants**: `AppConstants`, `AuthConstants`, `ConfigurationKeys`, `ConfigurationSections`, `ErrorCodes`, `RegexPatterns`
- **Extensions**: `ConfigurationExtensions`, `DateTimeExtensions`, `EnumerableExtensions`, `StringExtensions`
- **Helpers**: `AesHelper`, `HashHelper`

## Tests

- ❌ No test projects in the solution.

## Known Gaps

- No test projects exist — testing patterns not yet established.
- `EmailService` interface exists but implementation may be a stub — needs verification.
- No repository pattern — use cases query `IAppDbContext` directly.
- No domain events implemented (AggregateRoot is empty marker class).
- No audit trail / soft delete on domain entities.
- `User` domain entity exists separately from `ApplicationUser` — mapping approach needs clarification for future features.
