# Prompt: Implement an APCS Backend Feature

Use this prompt as a task starter. Replace the placeholders before submitting it to an agent.

## Input

- **Requirement:** `<behavior to add or change>`
- **Feature:** `<existing or proposed feature area>`
- **Acceptance criteria:** `<observable success and failure behavior>`
- **Out of scope:** `<boundaries>`
- **Relevant issue/use-case ID:** `<optional traceability metadata>`

## Request

Implement the requirement in the current APCS backend.

Before editing, follow [`../../AGENTS.md`](../../AGENTS.md), inspect the worktree, and read:

- [`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md)
- [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md)
- [`../../docs/TESTING.md`](../../docs/TESTING.md)

Inspect the target area and the closest implemented feature before proposing changes. State a short file/layer plan, then implement only the approved requirement while applying the linked architecture and contribution rules. Preserve unrelated changes.

Run focused tests and the solution test suite as appropriate. Finish with:

- changed behavior and files;
- verification performed and results;
- any architecture deviation, migration concern, or remaining gap.
