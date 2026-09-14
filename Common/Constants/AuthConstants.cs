namespace APCS.Common.Constants;

/// <summary>
/// Provides shared authentication constants.
/// </summary>
public static class AuthConstants
{
    /// <summary>
    /// The refresh token cookie name.
    /// </summary>
    public const string RefreshTokenCookieName = "__Host-apcs_refresh";

    /// <summary>
    /// The access token cookie name.
    /// </summary>
    public const string AccessTokenCookieName = "__Host-apcs_access";

    /// <summary>
    /// The default access token lifetime in minutes.
    /// </summary>
    public const int DefaultAccessTokenMinutes = 15;

    /// <summary>
    /// The default refresh token lifetime in days.
    /// </summary>
    public const int DefaultRefreshTokenDays = 14;

    /// <summary>
    /// The default email verification link lifetime in hours.
    /// </summary>
    public const int DefaultEmailVerificationHours = 24;

    /// <summary>
    /// The default password reset link lifetime in minutes.
    /// </summary>
    public const int DefaultPasswordResetMinutes = 10;

    /// <summary>
    /// The default application user role.
    /// </summary>
    public const string UserRole = "Seller";

    /// <summary>
    /// The administrative application role.
    /// </summary>
    public const string AdminRole = "Admin";

    /// <summary>
    /// The OAuth provider value recorded for accounts created or linked through Google Sign-In.
    /// </summary>
    public const string GoogleProvider = "google";
}
