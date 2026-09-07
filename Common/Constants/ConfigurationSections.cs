namespace APCS.Common.Constants;

/// <summary>
/// Provides canonical configuration section names.
/// </summary>
public static class ConfigurationSections
{
    /// <summary>
    /// The connection strings section.
    /// </summary>
    public const string ConnectionStrings = "ConnectionStrings";

    /// <summary>
    /// The JWT options section.
    /// </summary>
    public const string Jwt = "Jwt";

    /// <summary>
    /// The CORS options section.
    /// </summary>
    public const string Cors = "Cors";

    /// <summary>
    /// The logging section.
    /// </summary>
    public const string Logging = "Logging";

    /// <summary>
    /// The Redis cache section.
    /// </summary>
    public const string Redis = "Redis";

    /// <summary>
    /// The Google sign-in options section.
    /// </summary>
    public const string GoogleAuth = "Authentication:Google";
}
