using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Platform-level defaults for the Gemini image generation calls. The API key itself is
/// never here — it comes from the Seller's own BYOK <c>ApiKey</c> row.</summary>
public sealed class GeminiOptions
{
    public const string SectionName = ConfigurationSections.Gemini;

    public string DefaultModel { get; set; } = "gemini-3.1-flash-image";

    /// <summary>Placeholder estimate used only for the <c>ApiUsageRecord.CostUsd</c> display until a
    /// confirmed official per-image price is available.</summary>
    public decimal EstimatedCostPerImageUsd { get; set; } = 0.04m;

    /// <summary>How many images of one design generation job are requested at the same time, all on the
    /// Seller's own key. 1 makes one image at a time.</summary>
    public int MaxConcurrentImages { get; set; } = 4;

    /// <summary>How many times a call Gemini turned away as busy (429 or 503) is sent again.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>The wait before the first repeat of a busy call; it doubles for each further one.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
