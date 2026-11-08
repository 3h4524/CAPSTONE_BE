using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Configures the public Etsy API connector. Secrets are supplied through environment variables.</summary>
public sealed class EtsyOptions
{
    public const string SectionName = ConfigurationSections.Etsy;

    public string ApiKey { get; set; } = string.Empty;
    public string SharedSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.etsy.com/v3/application/";
    public int RequestTimeoutSeconds { get; set; } = 30;
    public bool LogRawResponses { get; set; }
    public int RawResponseLogMaxChars { get; set; } = 12000;

    /// <summary>Etsy's consent page for a seller connecting their shop.</summary>
    public string OAuthAuthorizeUrl { get; set; } = "https://www.etsy.com/oauth/connect";

    /// <summary>Etsy's token endpoint for code exchange and refresh.</summary>
    public string OAuthTokenUrl { get; set; } = "https://api.etsy.com/v3/public/oauth/token";

    /// <summary>
    /// The callback registered on the Etsy app; Etsy compares it character for character, so it must match
    /// the app setting exactly.
    /// </summary>
    public string OAuthRedirectUri { get; set; } = "http://localhost:5191/api/etsy/oauth/callback";

    /// <summary>
    /// Permissions asked of the seller: read the shop and its listings, read orders (revenue, order flow and
    /// fulfilment time on the shop page), and write listings so workflow assets can be pushed as drafts
    /// without asking again. A seller connected with fewer scopes must connect again to grant new ones.
    /// </summary>
    public string OAuthScopes { get; set; } = "shops_r listings_r listings_w transactions_r";

    /// <summary>
    /// The client page Etsy's callback sends the browser to, with the outcome added as <c>?etsy=</c>. It is a
    /// bare page: in the connect popup it hands the outcome to the shop page and closes; opened as a full
    /// page (popup blocked) it forwards to the shop page.
    /// </summary>
    public string ReturnPagePath { get; set; } = "/etsy/callback";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string ApiKeyHeader => string.IsNullOrWhiteSpace(SharedSecret)
        ? ApiKey
        : $"{ApiKey}:{SharedSecret}";
}
