using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Auth.Common;

/// <summary>
/// Builds every error the Auth feature can return, so wording and codes stay in one place.
/// </summary>
internal static class AuthErrors
{
    public static Error InvalidCredentials() =>
        Error.Unauthorized(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");

    public static Error EmailNotVerified() =>
        Error.Forbidden(
            ErrorCodes.EmailNotVerified,
            "The account email has not been verified. Check your inbox or request a new verification link.");

    public static Error Inactive() =>
        Error.Forbidden(ErrorCodes.UserInactive, "The account is inactive.");

    public static Error RefreshTokenMissing() =>
        Error.Unauthorized(ErrorCodes.RefreshTokenMissing, "Refresh token is missing.");

    public static Error RefreshTokenInvalid() =>
        Error.Unauthorized(ErrorCodes.RefreshTokenInvalid, "Refresh token is invalid.");

    public static Error RefreshTokenExpired() =>
        Error.Unauthorized(ErrorCodes.RefreshTokenExpired, "Refresh token has expired.");

    public static Error RefreshTokenReused() =>
        Error.Unauthorized(ErrorCodes.RefreshTokenReused, "Refresh token has already been revoked.");

    public static Error Unauthenticated() =>
        Error.Unauthorized(ErrorCodes.Unauthorized, "The request is not authenticated.");

    public static Error UserNotFound() =>
        Error.NotFound(ErrorCodes.UserNotFound, "User was not found.");

    public static Error GoogleTokenInvalid() =>
        Error.Unauthorized(ErrorCodes.GoogleTokenInvalid, "The Google sign-in could not be verified.");

    public static Error GoogleEmailNotVerified() =>
        Error.Forbidden(
            ErrorCodes.GoogleEmailNotVerified,
            "The Google account's email address is not verified.");

    public static Error RegistrationFailed(IReadOnlyCollection<string> reasons) =>
        Error.Validation(
            "Could not create the account.",
            new Dictionary<string, string[]> { ["identity"] = reasons.ToArray() });
}
