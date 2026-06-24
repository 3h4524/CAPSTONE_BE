namespace APCS.Common.Constants;

/// <summary>
/// Provides canonical error codes used by API and application results.
/// </summary>
public static class ErrorCodes
{
    public const string Validation = "validation.failed";
    public const string Unexpected = "system.unexpected";
    public const string Unauthorized = "auth.unauthorized";
    public const string Forbidden = "auth.forbidden";
    public const string EmailAlreadyExists = "auth.email_already_exists";
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string UserInactive = "auth.user_inactive";
    public const string UserLockedOut = "auth.user_locked_out";
    public const string RefreshTokenMissing = "auth.refresh_token_missing";
    public const string RefreshTokenInvalid = "auth.refresh_token_invalid";
    public const string RefreshTokenExpired = "auth.refresh_token_expired";
    public const string RefreshTokenReused = "auth.refresh_token_reused";
    public const string UserNotFound = "users.not_found";
}
