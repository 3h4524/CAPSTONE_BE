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
}
