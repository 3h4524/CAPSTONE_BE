# Backend Rules

## Clean Architecture

- Domain must not depend on Application, Infrastructure, or API.
- Application must not depend on Infrastructure or API.
- Infrastructure implements interfaces defined in Application.
- API is the composition root — it references Application and Infrastructure.
- Common is a shared utility layer — no business logic.
- Controllers must stay thin — call `ISender.Send()` and convert `Result` to `IActionResult`.

## Use Case Style

- Prefer one folder per use case under `Application/UseCases/{Feature}/{UCxx_Name}/`.
- Each folder contains: Command/Query, Handler, Validator (if needed), Response.
- Use the naming convention `UC01_Register`, `UC02_Login`, etc.
- Do not create large god services — split by use case.
- Use `Result<T>` for expected failures, exceptions for unexpected bugs.

## MediatR

- Commands for write operations, Queries for read operations.
- Handlers are the primary unit of application logic.
- Validation happens in `ValidationBehaviour` via FluentValidation — not in handlers.
- Logging happens in `LoggingBehaviour` — not in handlers.

## EF Core

- Use the existing `IAppDbContext` abstraction — do not inject `AppDbContext` directly.
- Do not return EF entities (`ApplicationUser`, etc.) directly from API responses — map to DTOs/responses.
- Use `AsNoTracking()` for read-only queries.
- Add migrations carefully — review generated SQL before applying.
- Use entity configurations in `Infrastructure/Persistence/Configurations/`.
- Connection string comes from environment — never hard-code.

## Security

- Do not hard-code secrets (JWT key, connection strings, API keys).
- Do not log tokens, passwords, or API keys.
- Do not expose password hashes or internal Identity details.
- Validate current user ownership for user-scoped resources via `ICurrentUser`.
- Use `[Authorize]` on protected endpoints — `[AllowAnonymous]` only for public routes.
- Refresh tokens are stored as SHA-256 hashes — never store raw tokens.

## Error Handling

- Use `Result<T>` / `Result` for expected application errors.
- Use `Error` with `ErrorType` (Validation, Unauthorized, Forbidden, NotFound, Conflict).
- Use `ResultExtensions.ToActionResult()` to map results to ProblemDetails responses.
- Let unexpected exceptions bubble to `GlobalExceptionMiddleware`.

## Verification

- Run `dotnet build` after code changes.
- Run `dotnet test` if test projects exist.
- Verify no secrets in committed code.
- Verify Clean Architecture dependency direction.
