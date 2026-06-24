# .agent/ — APCS Backend Agent Documentation

This folder contains lightweight documentation for AI coding agents working on the APCS backend.

## Structure

```
.agent/
├── README.md                          ← You are here
├── context/
│   ├── project.md                     ← What APCS is, current phase
│   └── current-state.md               ← Actual implementation state (scan-based)
├── rules/
│   └── backend-rules.md               ← Clean Architecture & coding rules
├── skills/
│   ├── dotnet-backend/SKILL.md        ← How to add a backend feature
│   └── auth-jwt/SKILL.md              ← JWT implementation details
├── workflows/
│   ├── scan-before-edit.md            ← Pre-edit checklist
│   ├── implement-feature.md           ← Feature implementation workflow
│   └── review-code.md                 ← Code review checklist
└── prompts/
    ├── implement-next-use-case.md     ← Reusable prompt for adding use cases
    └── fix-build-error.md             ← Reusable prompt for fixing build errors
```

## How to Use

1. Start with `CLAUDE.md` at the repo root for a quick overview.
2. Read `context/current-state.md` to understand what is already implemented.
3. Follow the relevant workflow before making changes.
4. Use skills as step-by-step guides for specific tasks.
5. Use prompts as copy-paste starting points for common requests.
