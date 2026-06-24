namespace APCS.Application.Common.Models;

/// <summary>
/// Represents a generated access token.
/// </summary>
public sealed record JwtTokenResult(
    string AccessToken,
    string JwtId,
    DateTimeOffset ExpiresAtUtc);
