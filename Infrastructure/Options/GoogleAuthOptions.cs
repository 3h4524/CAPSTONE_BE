using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>
/// Provides strongly typed Google Sign-In configuration.
/// </summary>
public sealed class GoogleAuthOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = ConfigurationSections.GoogleAuth;

    /// <summary>
    /// Gets or sets the OAuth client ID issued for this app in Google Cloud Console.
    /// </summary>
    /// <remarks>
    /// Left empty in environments that have not set up Google Sign-In; <see cref="APCS.Infrastructure.Services.GoogleAuthService"/>
    /// rejects every token when this is unset rather than failing application startup.
    /// </remarks>
    public string ClientId { get; set; } = string.Empty;
}
