using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Completes administrator login with the email OTP challenge.
/// </summary>
public sealed record AdminVerifyTwoFactorRequestDto(
    string TempToken,
    string OtpCode,
    RequestContext? Context = null);