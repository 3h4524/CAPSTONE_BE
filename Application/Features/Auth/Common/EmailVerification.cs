using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

/// <summary>
/// Carries a freshly issued email verification token and the row that will redeem it.
/// </summary>
/// <param name="Token">The raw token, emailed to the user and never persisted.</param>
/// <param name="Entity">The persisted token record holding only the hash.</param>
internal sealed record EmailVerification(string Token, AuthToken Entity);
