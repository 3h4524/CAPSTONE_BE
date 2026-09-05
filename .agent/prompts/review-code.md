# Prompt: Review APCS Backend Code

## Input

- **Review target:** `<diff, branch, commit, or files>`
- **Requirement/intent:** `<expected behavior>`
- **Focus:** `<optional security, architecture, persistence, API, or testing focus>`

## Request

Review the target without modifying files unless explicitly asked to implement fixes.

Follow [`../../AGENTS.md`](../../AGENTS.md) and evaluate the target against every canonical document its task table names for the change under review.

Inspect the actual surrounding implementation and tests; do not review from the diff alone. Prioritize correctness, security, data integrity, behavior regressions, dependency violations, and missing tests over style preferences.

Report findings first, ordered by severity. For every finding provide:

- severity and concise title;
- file and line reference;
- concrete failure mode or risk;
- smallest appropriate correction.

Separate confirmed defects from questions or optional improvements. If no findings remain, say so and identify any verification gap.
