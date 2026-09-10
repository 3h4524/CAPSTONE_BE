using System.Text.Json.Serialization;

namespace APCS.Application.Features.Auth.Dtos.Response;

/// <summary>
/// Represents a successful token refresh response.
/// </summary>
public sealed record RefreshTokenResponseDto(
    [property: JsonIgnore] string AccessToken,
    [property: JsonIgnore] DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset RefreshTokenExpiresAtUtc);
