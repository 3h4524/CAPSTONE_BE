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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string ApiKeyHeader => string.IsNullOrWhiteSpace(SharedSecret)
        ? ApiKey
        : $"{ApiKey}:{SharedSecret}";
}
