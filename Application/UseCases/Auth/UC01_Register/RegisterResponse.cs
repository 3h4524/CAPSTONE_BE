using System.Text.Json.Serialization;
using APCS.Application.UseCases.Auth.Common;

namespace APCS.Application.UseCases.Auth.UC01_Register;

/// <summary>
/// Represents a successful register response.
/// </summary>
public sealed record RegisterResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthenticatedUserResponse User);
