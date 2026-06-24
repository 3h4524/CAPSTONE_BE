# CLAUDE.md — APCS Backend Agent Guide

## Project Identity

APCS (AI-Powered POD Content Studio) backend built with ASP.NET Core 8, C#, PostgreSQL, and EF Core.
Architecture: Clean Architecture with MediatR CQRS.

## Current Status

- **Phase**: Foundation — project structure, authentication, and Redis cache are implemented.
- **Implemented**: JWT auth (register, login, refresh, logout, get-current-user), ASP.NET Core Identity, global exception handling, Result pattern, FluentValidation pipeline, Redis cache connection (ICacheService abstraction).
- **Not yet implemented**: Business domain features (API keys, batch upload, product queue, Hangfire).

## Architecture Rules

- **Domain** has zero dependencies on Application, Infrastructure, or API.
- **Application** depends only on Domain and Common.
- **Infrastructure** implements interfaces defined in Application.
- **API** is a thin host — delegates to Application via MediatR.
- **Common** is a shared utility layer (Result, Error, constants, helpers).

## Repository Structure

```
Capstone.sln
├── API/            → ASP.NET Core host, controllers, middleware (APCS.Api)
├── Application/    → Use cases, MediatR handlers, validators, interfaces (APCS.Application)
├── Domain/         → Entities, value objects, enums, exceptions (APCS.Domain)
├── Infrastructure/ → EF Core, Identity, JWT, external services (APCS.Infrastructure)
└── Common/         → Result pattern, Error, constants, helpers (APCS.Common)
```

## Coding Rules

- Keep controllers thin — they call `ISender.Send()` and map the `Result`.
- One folder per use case under `Application/UseCases/`.
- Each use case folder contains: Command/Query, Handler, Validator, Response.
- Use the `Result<T>` / `Result` pattern for expected failures — not exceptions.
- Use `FluentValidation` for input validation via the `ValidationBehaviour` pipeline.
- Pass `CancellationToken` through all async methods.
- Use `sealed` classes/records where possible.
- Use XML doc comments on public APIs.

## Security Rules

- **Never** hard-code secrets — JWT signing key comes from configuration/environment.
- **Never** expose password hashes, JWT secrets, or API keys in responses or logs.
- **Never** return `ApplicationUser` (Identity entity) directly — map to DTOs.
- Protect endpoints with `[Authorize]` — use `[AllowAnonymous]` only for public routes.
- Validate resource ownership for user-scoped resources using `ICurrentUser`.

## Before Editing

1. Read this file.
2. Read `.agent/context/current-state.md`.
3. Inspect the target area and similar existing code.
4. Make a short plan before writing code.

## After Editing

1. Run `dotnet build` from the solution root.
2. Run `dotnet test` if test projects exist.
3. Verify no secrets are committed.

## Common Commands

```bash
# Build
dotnet build

# Run API
dotnet run --project API

# Add EF migration (from solution root)
dotnet ef migrations add <Name> --project Infrastructure --startup-project API

# Apply migrations
dotnet ef database update --project Infrastructure --startup-project API

# Test
dotnet test

# Redis (local Docker)
docker compose up -d redis
docker exec -it apcs-redis redis-cli ping
```

## Redis Cache Rules

- Redis is **cache only** — PostgreSQL is the source of truth.
- Application uses `ICacheService` abstraction (in `Application/Common/Interfaces/`).
- Infrastructure implements Redis via `IDistributedCache` (`RedisCacheService`).
- Configuration via `Redis` section (`.env`: `Redis__ConnectionString`, `Redis__InstanceName`, `Redis__DefaultExpirationMinutes`).
- Local Redis runs through Docker Compose.
- **Never cache**: password hashes, JWT tokens, refresh tokens, API secrets.

## Definition of Done

- [ ] Code builds without errors or warnings.
- [ ] No secrets hard-coded.
- [ ] Clean Architecture dependencies respected.
- [ ] Controllers remain thin — logic in Application layer.
- [ ] Validation present for user input.
- [ ] CancellationToken passed through async stack.
- [ ] Tests pass (if test projects exist).

## Agent Docs

See `.agent/` for detailed context, rules, skills, workflows, and reusable prompts.
