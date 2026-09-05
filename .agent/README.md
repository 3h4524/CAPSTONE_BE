# APCS AI Tooling

`.agent/` contains APCS-specific tools for AI-assisted work. It is not the source of truth for project architecture or coding policy.

## Precedence

[`../AGENTS.md`](../AGENTS.md) holds the authority order and the table that decides which canonical document applies to a task. Files under `.agent/` provide task-specific inputs or execution help and remain subordinate to every source listed there.

## Artifacts

| Artifact | Use it when |
|---|---|
| [`prompts/implement-feature.md`](prompts/implement-feature.md) | Framing a feature or behavior change before editing starts |
| [`prompts/review-code.md`](prompts/review-code.md) | Reviewing a diff, branch, or commit without modifying files |
| [`prompts/create-tests.md`](prompts/create-tests.md) | Adding tests for existing behavior, starting from the unit/integration boundary |
| [`workflows/feature-development.md`](workflows/feature-development.md) | Running a feature end to end with intake, plan, verification, and handoff checkpoints |

`context/` and `skills/` hold no artifact yet. Read the folder responsibilities below before adding one.

## Folder responsibilities

### `prompts/`

Reusable task requests. A prompt may identify required inputs, expected output, and canonical documents to read. It must not copy or redefine project rules.

### `workflows/`

Agent execution sequences with useful checkpoints, tool ordering, stopping conditions, or handoff requirements. A workflow that merely repeats `CONTRIBUTING.md` should be merged into a prompt or removed.

### `context/`

Stable context that materially changes agent decisions and cannot be reliably derived from source. Each context file must state its owner, reason, and review or expiry condition.

Do not store scan dates, implemented-feature inventories, project trees, or planned-technology lists here. The folder is intentionally empty until qualifying context exists; empty directories may be absent from Git.

### `skills/`

Triggerable APCS-specific capabilities with non-obvious operational knowledge. Skills must be narrowly described, reference canonical documentation, and avoid vendoring generic framework manuals.

No skill is defined yet. The folder is created when a qualifying capability exists; empty directories may be absent from Git.

## Admission checklist

Add an artifact only when all answers are yes:

- Does it solve a repeated APCS-specific agent task?
- Does it add operational value beyond the canonical docs?
- Can it link to project policy instead of copying it?
- Is its owner or maintenance trigger clear?
- Does it avoid introducing unapproved technology or patterns?
