using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Issues a fresh verification email for an account that is still pending verification.
/// </summary>
/// <param name="Email">The registered email address.</param>
/// <param name="Context">The caller's network details, set by the API layer.</param>
/// <remarks>Use case mapping: UC11.</remarks>
public sealed record ResendVerificationEmailRequestDto(
    string Email,
    RequestContext? Context = null);
