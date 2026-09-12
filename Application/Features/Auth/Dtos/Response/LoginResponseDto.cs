using System.Text.Json.Serialization;
using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Response;

/// <summary>
/// Represents a successful login response.
/// </summary>
public sealed record LoginResponseDto(
    [property: JsonIgnore] string AccessToken,
    [property: JsonIgnore] DateTimeOffset ExpiresAtUtc,
    [property: JsonIgnore] string RefreshToken,
    [property: JsonIgnore] DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthenticatedUserResponse User);
