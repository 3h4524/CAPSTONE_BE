namespace APCS.Application.Features.Auth.Dtos.Request;

/// <summary>
/// Changes the authenticated user's password.
/// </summary>
/// <param name="CurrentPassword">The account's current password for verification.</param>
/// <param name="NewPassword">The new password to set.</param>
/// <remarks>Use case mapping: UC-ChangePassword.</remarks>
public sealed record ChangePasswordRequestDto(
    string CurrentPassword,
    string NewPassword);
