using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Configures the Pinterest API v5 connector. The app secret is supplied through environment variables.</summary>
public sealed class PinterestOptions
{
    public const string SectionName = ConfigurationSections.Pinterest;

    /// <summary>The app ID from developers.pinterest.com (the OAuth client id).</summary>
    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// Where boards are read and pins created. An app with Trial access may only create pins on the sandbox,
    /// <c>https://api-sandbox.pinterest.com/v5/</c>; Standard access uses the production API.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.pinterest.com/v5/";

    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Pinterest's consent page for a seller connecting their account.</summary>
    public string OAuthAuthorizeUrl { get; set; } = "https://www.pinterest.com/oauth/";

    /// <summary>Pinterest's token endpoint for code exchange and refresh; always on the production API.</summary>
    public string OAuthTokenUrl { get; set; } = "https://api.pinterest.com/v5/oauth/token";

    /// <summary>The redirect URI registered on the Pinterest app; it must match the app setting exactly.</summary>
    public string OAuthRedirectUri { get; set; } = "http://localhost:5191/api/pinterest/oauth/callback";

    /// <summary>Read the account and its boards, and create pins.</summary>
    public string OAuthScopes { get; set; } = "user_accounts:read,boards:read,pins:read,pins:write";

    /// <summary>
    /// The client page the callback sends the browser to, with the outcome added as <c>?pinterest=</c>. In the
    /// connect popup it hands the outcome to the page that opened it and closes.
    /// </summary>
    public string ReturnPagePath { get; set; } = "/pinterest/callback";

    /// <summary>A pin's public page is this followed by its id.</summary>
    public string PinUrlBase { get; set; } = "https://www.pinterest.com/pin/";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);
}
