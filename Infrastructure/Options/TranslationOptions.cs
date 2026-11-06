namespace APCS.Infrastructure.Options;

/// <summary>
/// Configuration for the server-side machine translation provider.
/// </summary>
public sealed class TranslationOptions
{
    public const string SectionName = "Translation";

    public string Provider { get; set; } = "google";

    public string ApiKey { get; set; } = string.Empty;

    public string Endpoint { get; set; } = "https://translation.googleapis.com/language/translate/v2";
}
