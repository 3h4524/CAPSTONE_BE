using System.Text.Json.Serialization;
using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Commands.Login;

/// <summary>
/// Represents a successful login response.
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthenticatedUserResponse User);
