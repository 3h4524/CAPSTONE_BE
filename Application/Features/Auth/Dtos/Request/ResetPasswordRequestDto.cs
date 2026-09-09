namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Redeems a password reset token and sets a new password.
/// </summary>
/// <param name="Token">The raw reset token from the emailed link.</param>
/// <param name="NewPassword">The new password to set.</param>
/// <remarks>Use case mapping: UC-ResetPassword.</remarks>
public sealed record ResetPasswordRequestDto(
    string Token,
    string NewPassword);
