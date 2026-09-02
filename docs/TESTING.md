# APCS Backend Testing

This document is the source of truth for test boundaries, placement, conventions, and verification commands.

## Current test layout

The unit-test projects mirror production assemblies:

| Project | Primary scope |
|---|---|
| `APCS.Common.UnitTests` | Result/error primitives, helpers, extensions, and wrappers |
| `APCS.Domain.UnitTests` | Value objects and domain behavior |
| `APCS.Application.UnitTests` | Handlers, validators, pipeline behaviours, expected outcomes, and side effects |
| `APCS.Infrastructure.UnitTests` | Isolated adapters, options validation, and service registration behavior |
| `APCS.Api.UnitTests` | Controllers, result mapping, cookies, and exception middleware in isolation |

The projects use MSTest, FluentAssertions, and Moq. Tests that need a controllable clock use `FakeTimeProvider`.

## Unit versus integration tests

Use unit tests when behavior can be verified without a real external system:

- Domain invariants, transitions, and value objects.
- Application handler decisions, repository/service calls, expected failures, and cancellation propagation.
- Validators and MediatR pipeline behaviours.
- Serialization-independent adapter behavior with a stable abstraction.
- Controller request mapping, result mapping, cookies, and other isolated HTTP behavior.

Use integration tests when correctness depends on framework/provider behavior:

- PostgreSQL query translation, constraints, indexes, mappings, transactions, or migrations.
- EF Core tracking, concurrency, query filters, and relationships.
- Redis expiration, serialization interoperability, connectivity, or invalidation behavior.
- Dependency injection across the full host.
- Authentication, authorization, middleware ordering, routing, and complete HTTP request/response behavior.

No integration-test project exists yet. Do not label a mock-based test as integration coverage. Introduce integration infrastructure only as part of an approved change and use disposable real PostgreSQL/Redis services appropriate to the test.

## Test conventions

- Mirror the production namespace and folder beneath the corresponding test project.
- Name tests `Method_Scenario_ExpectedResult`, such as `Handle_WhenEmailExists_ReturnsConflict`.
- Keep Arrange, Act, and Assert conceptually distinct; comments are optional when the phases are already clear.
- Test public behavior rather than private methods or implementation details.
- Cover success, expected failure, boundary values, state transitions, and forbidden side effects.
- Verify that the original `CancellationToken` reaches asynchronous dependencies when relevant.
- Use deterministic clocks and data. Do not depend on the machine clock, network, file system, PostgreSQL, or Redis in a unit test.
- A bug fix requires a regression test that fails for the original behavior.
- Do not add tests for trivial getters, navigation properties, or data-only constructors solely to increase coverage.

## EF Core test boundary

Do not mock `DbSet` and do not use EF Core InMemory as a substitute for PostgreSQL. Both approaches can hide translation, constraint, transaction, and provider differences.

For an Application unit test, mock the focused repository, `IUnitOfWork`, or another Application abstraction. For a query whose behavior depends on actual EF translation, add a PostgreSQL integration test when that infrastructure exists.

## Adding tests for a feature

1. Identify the behavior and the layer that owns it.
2. Add tests to the mirrored test project and namespace.
3. Cover the successful path, each meaningful expected failure, boundary inputs, state changes, and absence of forbidden calls.
4. Test validators separately when their rule matrix is non-trivial.
5. Add provider or HTTP integration coverage when mocks cannot prove the behavior.
6. If a new behavioral namespace or Domain type is added, include it in the behavioral class filter in `scripts/Test-WithCoverage.ps1`.
7. Run the focused test project, then the solution suite.

## Commands and coverage

Run all current tests:

```powershell
dotnet test Capstone.sln
```

Run one test project while iterating:

```powershell
dotnet test tests/APCS.Application.UnitTests/APCS.Application.UnitTests.csproj
```

Run the Release build, full coverage report, and behavioral gate used by CI:

```powershell
./scripts/Test-WithCoverage.ps1
```

The behavioral gate requires at least 80% line coverage and 70% branch coverage. `scripts/Test-WithCoverage.ps1` and `.github/workflows/backend-ci.yml` enforce the gate; this document explains how to work with it.

Generated reports belong under `artifacts/coverage` and are not committed.
