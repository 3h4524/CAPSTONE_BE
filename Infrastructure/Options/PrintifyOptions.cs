using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Configures the Printify API adapter. There is no server secret: each seller supplies a personal access token.</summary>
public sealed class PrintifyOptions
{
    public const string SectionName = ConfigurationSections.Printify;

    public string BaseUrl { get; set; } = "https://api.printify.com/v1/";

    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>Printify requires a User-Agent naming the client on every request.</summary>
    public string UserAgent { get; set; } = "APCS-Capstone";
}
