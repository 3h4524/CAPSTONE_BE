# Workflow: AI-Assisted Feature Development

This workflow adds agent-specific checkpoints to the human process in [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md). Architecture and testing decisions remain authoritative in the linked canonical documents.

## 1. Intake checkpoint

- Restate the observable requirement, success criteria, and scope boundary.
- Identify unresolved product decisions that cannot be discovered from the repository.
- Stop for clarification only when a reasonable assumption could materially change behavior, security, data, or public contracts.

## 2. Repository scan

- Read [`../../AGENTS.md`](../../AGENTS.md), [`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md), and [`../../docs/TESTING.md`](../../docs/TESTING.md).
- Inspect `git status`, project references, the target code, and the closest implemented feature/test.
- Record user-owned changes that must be preserved.
- Confirm whether the request needs Domain, Application, Infrastructure, API, migration, and test changes.

## 3. Plan checkpoint

- List the intended behavior changes and files by layer.
- Identify command/query classification, persistence boundary, authorization, expected failures, and test boundary.
- Call out any existing architecture deviation the work will touch.
- Avoid opportunistic cleanup outside the requirement.

## 4. Execution checkpoint

- Implement in the smallest coherent increments, following the Adding a New Feature section in `CONTRIBUTING.md`.
- Re-read adjacent code before changing an interface, public contract, migration, authentication flow, or persistence boundary.
- Keep user-visible status updates concise during long-running work.
- Stop and report if the required change expands authority, risks unrelated data, or depends on an unresolved contract.

## 5. Verification checkpoint

- Run focused tests after the relevant layer is complete.
- Run `dotnet test Capstone.sln` for the completed behavior.
- Run `./scripts/Test-WithCoverage.ps1` when behavior or the coverage filter changes.
- Review failures before changing production code; do not weaken assertions or boundaries merely to make tests pass.

## 6. Diff audit and handoff

- Inspect the final diff for unrelated edits, generated artifacts, secrets, stale paths, and documentation duplication.
- Confirm canonical documentation changed only when a durable contract or rule changed.
- Report the outcome, tests run, known deviations, migration impact, and remaining follow-up.
