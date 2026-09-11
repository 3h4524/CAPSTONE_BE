namespace APCS.Application.Features.Auth.Dtos.Response;

/// <summary>
/// Describes a pending administrator two-factor challenge.
/// </summary>
public sealed record AdminTwoFactorResponseDto(
    string TempToken,
    DateTimeOffset ExpiresAtUtc);