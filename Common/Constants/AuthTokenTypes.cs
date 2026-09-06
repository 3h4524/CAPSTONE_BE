namespace APCS.Common.Constants;

/// <summary>
/// Provides the token kinds stored in the auth-token table.
/// </summary>
public static class AuthTokenTypes
{
    /// <summary>
    /// A refresh token backing an issued access token.
    /// </summary>
    public const string Refresh = "refresh";

    /// <summary>
    /// A single-use password reset token.
    /// </summary>
    public const string PasswordReset = "password_reset";

    /// <summary>
    /// A single-use email verification token.
    /// </summary>
    public const string EmailVerification = "email_verification";

    /// <summary>
    /// Gets every valid token type.
    /// </summary>
    public static IReadOnlyCollection<string> All { get; } =
        [Refresh, PasswordReset, EmailVerification];
}
