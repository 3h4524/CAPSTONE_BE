## Summary

Describe the backend behavior changed by this pull request and any important architecture decision.

## Verification

- [ ] The change follows [Architecture](../docs/ARCHITECTURE.md) and [Contributing](../CONTRIBUTING.md), or the deviation is explained below.
- [ ] New or changed behavior has the tests required by the [Testing guide](../docs/TESTING.md).
- [ ] Bug fixes include a regression test.
- [ ] Async dependencies receive the original `CancellationToken` where applicable.
- [ ] `dotnet test Capstone.sln` passes locally.
- [ ] The behavioral coverage gate passes when affected.
- [ ] No password, JWT, refresh token, API key, connection string, or other secret is logged or committed.

## Exceptions or follow-up

Explain any intentionally deferred test, known deviation, migration risk, or follow-up work.
