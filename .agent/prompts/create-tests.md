# Prompt: Create APCS Backend Tests

## Input

- **Behavior under test:** `<requirement or production type>`
- **Changed files:** `<paths or diff>`
- **Known scenarios:** `<optional cases>`

## Request

Add tests for the specified behavior without changing production behavior unless a required testability fix is explicitly authorized.

Follow [`../../AGENTS.md`](../../AGENTS.md) and [`../../docs/TESTING.md`](../../docs/TESTING.md). Inspect the matching production code, its closest existing tests, and the current test project configuration.

First classify each scenario as a unit or integration test. Add coverage for successful behavior, meaningful expected failures, boundaries, state changes, forbidden side effects, regression behavior, and cancellation where applicable. Apply the conventions and boundaries in the testing guide.

If required integration infrastructure does not exist, report the uncovered provider or pipeline behavior instead of substituting a different test level.

Run the focused test project and then `dotnet test Capstone.sln`. Report added scenarios, command results, and any remaining integration gap.
