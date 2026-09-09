using System.ComponentModel.DataAnnotations;
using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>
/// Provides strongly typed client application configuration.
/// </summary>
/// <remarks>
/// Holds the addresses used to build links that are emailed to users. The client owns those
/// pages, so the address belongs in configuration rather than in a use case.
/// </remarks>
public sealed class AppOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = ConfigurationSections.App;

    /// <summary>
    /// Gets or sets the client application base address.
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// Gets or sets the client path that redeems an email verification token.
    /// </summary>
    [Required]
    public string VerifyEmailPath { get; set; } = "/verify-email";

    /// <summary>
    /// Gets or sets the client path that redeems a password reset token.
    /// </summary>
    [Required]
    public string ResetPasswordPath { get; set; } = "/reset-password";
}
