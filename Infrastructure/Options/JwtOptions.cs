using System.ComponentModel.DataAnnotations;
using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>
/// Provides strongly typed JWT configuration.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = ConfigurationSections.Jwt;

    /// <summary>
    /// Gets or sets the token issuer.
    /// </summary>
    [Required]
    public string Issuer { get; set; } = "APCS";

    /// <summary>
    /// Gets or sets the token audience.
    /// </summary>
    [Required]
    public string Audience { get; set; } = "APCS.Web";

    /// <summary>
    /// Gets or sets the symmetric signing key.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets access token lifetime in minutes.
    /// </summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = AuthConstants.DefaultAccessTokenMinutes;

    /// <summary>
    /// Gets or sets refresh token lifetime in days.
    /// </summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = AuthConstants.DefaultRefreshTokenDays;
}
