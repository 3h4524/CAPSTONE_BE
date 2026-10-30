using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Platform-level defaults for the Gemini image generation calls. The API key itself is
/// never here — it comes from the Seller's own BYOK <c>ApiKey</c> row.</summary>
public sealed class GeminiOptions
{
    public const string SectionName = ConfigurationSections.Gemini;

    public string DefaultModel { get; set; } = "gemini-3.1-flash-image";

    /// <summary>Google's price for one image from the default model, recorded as <c>ApiUsageRecord.CostUsd</c>:
    /// a 1K image is 1,120 output tokens at $60 per 1M ($0.0672), plus the prompt and any input images
    /// (1,120 tokens each at $0.50 per 1M, about $0.0006).</summary>
    public decimal EstimatedCostPerImageUsd { get; set; } = 0.068m;

    /// <summary>How many images of one design generation job are requested at the same time, all on the
    /// Seller's own key. 1 makes one image at a time.</summary>
    public int MaxConcurrentImages { get; set; } = 4;

    /// <summary>How many times a call Gemini turned away as busy (429 or 503) is sent again.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>The wait before the first repeat of a busy call; it doubles for each further one.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
