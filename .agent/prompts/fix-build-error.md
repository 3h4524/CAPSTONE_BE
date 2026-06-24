# Prompt: Fix Build Error

Read the build error carefully.

Do not rewrite unrelated code.

**Steps:**

1. Identify the failing project (API, Application, Domain, Infrastructure, Common).
2. Identify the exact file and line from the error message.
3. Inspect nearby code for context.
4. Fix the smallest possible cause — do not refactor unrelated code.
5. Run `dotnet build` again.
6. Repeat until build passes.
7. Summarize the fix — what was wrong and what you changed.
