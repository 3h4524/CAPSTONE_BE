# Prompt: Implement Next Use Case

Read `CLAUDE.md` and `.agent/context/current-state.md`.

Then implement the requested backend use case following the existing Clean Architecture style.

**Before coding:**
- Inspect similar existing code (e.g., `Application/UseCases/Auth/` for structure).
- Explain a short plan listing which files you will create/modify.

**While coding:**
- Keep controllers thin — only `ISender.Send()` and `result.ToActionResult(this)`.
- Put use case logic in Application (`Command/Query` + `Handler` + `Validator` + `Response`).
- Put persistence/external service implementations in Infrastructure.
- Keep Domain clean — only entities, value objects, and domain logic.
- Add FluentValidation validators for input.
- Use `Result<T>` for expected business failures.
- Pass `CancellationToken` through all async methods.
- Do not hard-code secrets.
- Do not return EF entities directly.

**After coding:**
- Run `dotnet build`.
- Run `dotnet test` if test projects exist.
- Summarize changed/created files.
- Update `.agent/context/current-state.md` if appropriate.
