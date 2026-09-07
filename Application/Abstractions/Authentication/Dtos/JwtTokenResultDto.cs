namespace APCS.Application.Abstractions.Authentication.Dtos;

/// <summary>
/// Represents a generated access token.
/// </summary>
public sealed record JwtTokenResultDto(
    string AccessToken,
    string JwtId,
    DateTimeOffset ExpiresAtUtc);
