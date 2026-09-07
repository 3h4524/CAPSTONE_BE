using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Auth.Common;

/// <summary>
/// Builds every error the Auth feature can return, so wording and codes stay in one place.
/// </summary>
internal static class AuthErrors
{
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

    public static Error VerificationTokenInvalid() =>
        Error.Unauthorized(ErrorCodes.VerificationTokenInvalid, "The verification link is invalid.");

    public static Error VerificationTokenExpired() =>
        Error.Unauthorized(
            ErrorCodes.VerificationTokenExpired,
            "The verification link has expired. Request a new verification email.");

    public static Error Unauthenticated() =>
        Error.Unauthorized(ErrorCodes.Unauthorized, "The request is not authenticated.");

    public static Error UserNotFound() =>
        Error.NotFound(ErrorCodes.UserNotFound, "User was not found.");
}
