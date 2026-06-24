using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.UseCases.Auth.Common;

internal static class AuthErrors
{
    public static Error InvalidCredentials() =>
        Error.Unauthorized(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");

    public static Error LockedOut() =>
        Error.Forbidden(ErrorCodes.UserLockedOut, "The account is currently locked.");

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
}
