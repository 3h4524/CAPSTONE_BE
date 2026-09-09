namespace APCS.Common.Constants;

/// <summary>
/// Provides canonical configuration keys used by the application.
/// </summary>
public static class ConfigurationKeys
{
    /// <summary>
    /// Connection string keys.
    /// </summary>
    public static class ConnectionStrings
    {
        /// <summary>
        /// The default PostgreSQL database connection string key.
        /// </summary>
        public const string DefaultConnection = "DefaultConnection";
    }

    /// <summary>
    /// JWT configuration keys.
    /// </summary>
    public static class Jwt
    {
        public const string Issuer = $"{ConfigurationSections.Jwt}:Issuer";
        public const string Audience = $"{ConfigurationSections.Jwt}:Audience";
        public const string SigningKey = $"{ConfigurationSections.Jwt}:SigningKey";
        public const string AccessTokenMinutes = $"{ConfigurationSections.Jwt}:AccessTokenMinutes";
        public const string RefreshTokenDays = $"{ConfigurationSections.Jwt}:RefreshTokenDays";
    }

    /// <summary>
    /// CORS configuration keys.
    /// </summary>
    public static class Cors
    {
        public const string AllowedOrigins = $"{ConfigurationSections.Cors}:AllowedOrigins";
    }
}
