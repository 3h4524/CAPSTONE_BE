using System.Text.Json.Serialization;
using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Commands.Register;

/// <summary>
/// Represents a successful register response.
/// </summary>
public sealed record RegisterResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthenticatedUserResponse User);
