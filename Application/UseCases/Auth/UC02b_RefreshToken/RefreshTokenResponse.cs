using System.Text.Json.Serialization;

namespace APCS.Application.UseCases.Auth.UC02b_RefreshToken;

/// <summary>
/// Represents a successful token refresh response.
/// </summary>
public sealed record RefreshTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset RefreshTokenExpiresAtUtc);
