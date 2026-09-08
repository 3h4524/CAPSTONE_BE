using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Logs a user in (or registers one) using a Google Sign-In ID token minted by the client.
/// </summary>
public sealed record GoogleLoginRequestDto(
    string IdToken,
    RequestContext? Context = null);
