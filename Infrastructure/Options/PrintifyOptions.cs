using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>
/// Configures the Printify API adapter. Each seller supplies a personal access token; the only server secret is
/// <see cref="CatalogToken"/>, which admins use to make templates of Printify products.
/// </summary>
public sealed class PrintifyOptions
{
    public const string SectionName = ConfigurationSections.Printify;

    public string BaseUrl { get; set; } = "https://api.printify.com/v1/";

    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Printify requires a User-Agent naming the client on every request.</summary>
    public string UserAgent { get; set; } = "APCS-Capstone";

    /// <summary>
    /// Enlarges a design much smaller than its print with Cloudinary's AI upscale instead of plain scaling. Off by
    /// default: it counts against the Cloudinary plan. A file that fails to deliver falls back to the original design.
    /// </summary>
    public bool AiUpscale { get; set; }

    /// <summary>
    /// The platform's own Printify token (<c>Printify__CatalogToken</c>, kept out of appsettings). Admins read the catalog
    /// with it and make throwaway products in its first store to measure Printify's mock-ups. Empty turns that off.
    /// </summary>
    public string? CatalogToken { get; set; }
}
