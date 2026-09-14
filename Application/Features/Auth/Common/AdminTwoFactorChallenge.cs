namespace APCS.Application.Features.Auth.Common;

/// <summary>
/// Short-lived state for an administrator email OTP challenge.
/// </summary>
public sealed record AdminTwoFactorChallenge(
    Guid UserId,
    string Email,
    string FullName,
    string OtpHash,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    RequestContext RequestContext);