# Skill: Dotnet Backend Feature

## When to Use

Use this when adding a new backend feature or use case to the APCS backend.

## Steps

1. Read `CLAUDE.md` at the repo root.
2. Read `.agent/context/current-state.md` to understand existing patterns.
3. Inspect similar existing code (e.g., look at `Application/UseCases/Auth/` for use case structure).
4. Identify the correct layer for each piece of the feature:
   - **Domain**: entities, value objects, enums, domain logic
   - **Application**: use case command/query, handler, validator, response, interfaces
   - **Infrastructure**: EF persistence, external service implementations
   - **API**: controller endpoint, request DTOs
5. Add or update **Application** use case:
   - Create folder: `Application/UseCases/{Feature}/{UCxx_Name}/`
   - Add Command/Query record implementing `IRequest<Result<TResponse>>`
   - Add Handler implementing `IRequestHandler<TCommand, Result<TResponse>>`
   - Add Validator using FluentValidation (if input validation needed)
   - Add Response record
6. Add or update **Domain** model only if a new entity/value object is needed.
7. Add or update **Infrastructure** only if persistence or external service access is needed.
   - Add entity configuration in `Infrastructure/Persistence/Configurations/`
   - Add `DbSet<>` to `IAppDbContext` and `AppDbContext`
   - Create EF migration
8. Add or update **API** endpoint:
   - Add action method to existing or new controller
   - Use `ISender.Send()` to dispatch command/query
   - Use `result.ToActionResult(this)` to return HTTP response
   - Add `[Authorize]` or `[AllowAnonymous]` as appropriate
   - Add XML doc comments and `[ProducesResponseType]` attributes
9. Add validation via FluentValidation.
10. Add tests if the project already has a test pattern.
11. Run build/test.

## Rules

- Keep controllers thin — only request mapping and `ISender.Send()`.
- Do not bypass Application layer — controllers must not call Infrastructure directly.
- Do not hard-code secrets.
- Do not return EF entities directly — create response DTOs.
- Pass `CancellationToken` through all async methods.
- Use `Result<T>` for expected failures.

## Verification

```bash
dotnet restore
dotnet build
dotnet test
```
