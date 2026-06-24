# Workflow: Implement Feature

Follow this workflow when adding a new feature to the APCS backend.

1. **Understand** the requested feature — clarify requirements if ambiguous.
2. **Identify affected layers**:
   - Does it need a new domain entity or value object? → Domain
   - Does it need a new use case? → Application
   - Does it need database persistence or external service? → Infrastructure
   - Does it need a new API endpoint? → API
3. **Follow existing conventions**:
   - Use case folder: `Application/UseCases/{Feature}/{UCxx_Name}/`
   - Result pattern for outcomes
   - FluentValidation for input validation
   - `ISender.Send()` in controllers
4. **Implement small focused changes** — one use case at a time.
5. **Add validation and error handling**:
   - FluentValidation validator for command/query input
   - `Result<T>` for expected business errors
   - `[Authorize]` for protected endpoints
6. **Run build/test**:
   ```bash
   dotnet build
   dotnet test
   ```
7. **Update `.agent/context/current-state.md`** if the feature changes the project state significantly.
