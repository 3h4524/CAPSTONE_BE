# Workflow: Scan Before Editing

Always follow this workflow before making changes to the APCS backend.

1. Read `CLAUDE.md` at the repo root.
2. Read `.agent/context/current-state.md` to understand what exists.
3. Inspect the target files you plan to modify.
4. Inspect similar existing implementations (e.g., look at auth use cases for patterns).
5. Make a short plan — list what you will create/modify and in which layers.
6. Then edit.

## Why

The project uses specific conventions (use case folder structure, Result pattern, MediatR pipeline, etc.) that must be followed consistently. Skipping the scan step leads to inconsistent patterns and wasted refactoring.
