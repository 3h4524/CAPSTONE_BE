# APCS Backend

APCS (AI-Powered POD Content Studio) is a graduation-project backend for creating and managing Print-on-Demand content. The backend is an ASP.NET Core 8 Web API organized with Clean Architecture, MediatR CQRS, Entity Framework Core, PostgreSQL, ASP.NET Core Identity, custom JWT authentication, and Redis caching.

## Current capabilities

- Register, login, refresh-token rotation, logout, and current-user endpoints.
- ASP.NET Core Identity backed by PostgreSQL with integer seller identifiers.
- Hashed refresh-token storage, revocation, and optimistic concurrency handling.
- MediatR command/query handlers with FluentValidation and logging pipeline behaviours.
- Global exception handling and `ProblemDetails` responses.
- EF Core mappings and migrations for the current APCS data model.
- Redis-backed `ICacheService`, Swagger/OpenAPI, CORS, and a health endpoint.
- Five MSTest unit-test projects with a behavioral coverage gate in CI.

The business data model is broader than the implemented use cases. Most non-authentication features do not yet have Application handlers or API endpoints. `EmailService` is currently a development logging adapter, and the repository does not yet contain integration-test infrastructure for PostgreSQL, Redis, migrations, authorization, or the complete HTTP pipeline.

## Repository map

| Path | Responsibility |
|---|---|
| `API/` | ASP.NET Core host, controllers, HTTP contracts, middleware, authentication, and response mapping |
| `Application/` | Feature-oriented commands, queries, handlers, validators, responses, and external abstractions |
| `Domain/` | Entities, value objects, domain behavior, enums, and domain exceptions |
| `Infrastructure/` | EF Core, PostgreSQL, Identity, JWT, Redis, email, and other adapters |
| `Common/` | Shared result/error primitives, constants, helpers, and general extensions |
| `tests/` | Unit tests mirroring the production assemblies |
| `.agent/` | APCS-specific AI prompts, workflows, and skills; not a policy source |

Read [Architecture](docs/ARCHITECTURE.md) before changing layer boundaries or adding a use case. See [Contributing](CONTRIBUTING.md) for the development workflow and [Testing](docs/TESTING.md) for test placement.

## Prerequisites

- .NET 8 SDK. The exact patch version is pinned by [`global.json`](global.json); install that version or a later patch.
- PostgreSQL reachable through `ConnectionStrings__DefaultConnection`.
- Redis. The repository's Docker Compose file runs Redis 7 locally.
- Docker Desktop or another container runtime if using the provided Redis service.

## Local setup

From the solution root:

```powershell
Copy-Item .env.example .env
docker compose up -d redis
dotnet tool restore
dotnet restore Capstone.sln
```

Edit `.env` with a reachable PostgreSQL connection and a development-only JWT signing key. `.env` is ignored by Git; never commit it.

Apply the current migrations:

```powershell
dotnet ef database update --project Infrastructure --startup-project API
```

Start the API:

```powershell
dotnet run --project API
```

The development profiles use `https://localhost:7148` and `http://localhost:5191`. Swagger is available at `/swagger` in Development, and the health endpoint is `/health`.

## Verification

Run the fast test suite:

```powershell
dotnet test Capstone.sln
```

Run the same Release build, coverage collection, and quality gate used by CI:

```powershell
./scripts/Test-WithCoverage.ps1
```

Coverage reports are generated under the ignored `artifacts/coverage` directory.

## Documentation

- [Architecture and CQRS rules](docs/ARCHITECTURE.md)
- [Contributing and adding a feature](CONTRIBUTING.md)
- [Testing strategy](docs/TESTING.md)
- [AI-agent instructions](AGENTS.md)
- [AI-tooling catalog](.agent/README.md)
