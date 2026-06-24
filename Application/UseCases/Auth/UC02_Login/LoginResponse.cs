using System.Text.Json.Serialization;
using APCS.Application.UseCases.Auth.Common;

namespace APCS.Application.UseCases.Auth.UC02_Login;

/// <summary>
/// Represents a successful login response.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthenticatedUserResponse User);
