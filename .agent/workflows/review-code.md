# Workflow: Review Code

Use this checklist when reviewing code changes to the APCS backend.

## Check

- [ ] **Build errors** — does `dotnet build` pass?
- [ ] **Clean Architecture violations** — does Domain depend on Application/Infrastructure/API?
- [ ] **Controller business logic** — are controllers thin (only `ISender.Send()` + result mapping)?
- [ ] **Missing validation** — does user input have a FluentValidation validator?
- [ ] **Missing ownership checks** — are user-scoped resources checked via `ICurrentUser`?
- [ ] **Secret leakage** — are any secrets, tokens, or password hashes exposed in responses or logs?
- [ ] **EF entities returned directly** — are `ApplicationUser` or other EF entities returned from API?
- [ ] **Missing CancellationToken** — is `CancellationToken` passed through async methods?
- [ ] **Result pattern** — are expected failures returned as `Result.Failure()` (not thrown as exceptions)?
- [ ] **Naming conventions** — do use case folders follow `UCxx_Name` pattern?
- [ ] **Test coverage** — are tests added where applicable (when test project exists)?
