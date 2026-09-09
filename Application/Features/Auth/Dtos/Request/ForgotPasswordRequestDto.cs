using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Requests a password reset email for an account.
/// </summary>
/// <param name="Email">The registered email address.</param>
/// <param name="Context">The caller's network details, set by the API layer.</param>
/// <remarks>Use case mapping: UC-ForgotPassword.</remarks>
public sealed record ForgotPasswordRequestDto(
    string Email,
    RequestContext? Context = null);

