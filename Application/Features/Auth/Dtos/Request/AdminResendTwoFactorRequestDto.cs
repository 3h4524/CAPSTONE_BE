using APCS.Application.Features.Auth.Common;

namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Requests a replacement administrator email OTP.
/// </summary>
public sealed record AdminResendTwoFactorRequestDto(
	string TempToken,
	RequestContext? Context = null);