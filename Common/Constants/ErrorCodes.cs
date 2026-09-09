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
    public const string UserInactive = "auth.user_inactive";
    public const string RefreshTokenMissing = "auth.refresh_token_missing";
    public const string RefreshTokenInvalid = "auth.refresh_token_invalid";
    public const string RefreshTokenExpired = "auth.refresh_token_expired";
    public const string RefreshTokenReused = "auth.refresh_token_reused";
    public const string EmailNotVerified = "auth.email_not_verified";
    public const string VerificationTokenInvalid = "auth.verification_token_invalid";
    public const string VerificationTokenExpired = "auth.verification_token_expired";
    public const string PasswordResetTokenInvalid = "auth.password_reset_token_invalid";
    public const string PasswordResetTokenExpired = "auth.password_reset_token_expired";
    public const string PasswordIncorrect = "auth.password_incorrect";
    public const string UserNotFound = "users.not_found";
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string GoogleTokenInvalid = "auth.google_token_invalid";
    public const string GoogleEmailNotVerified = "auth.google_email_not_verified";
}
