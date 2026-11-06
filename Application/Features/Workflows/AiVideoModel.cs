namespace APCS.Application.Features.Workflows;

/// <summary>Default model for newly planned AI video scenes.</summary>
public static class AiVideoModel
{
    public const string ProviderKey = "minimax";
    public const string ModelKey = "minimax_h3";
    public const string ModelName = "MiniMax-H3";
    public const string Resolution = "768P";
    public const decimal PricePerSecondUsd = 0.08m;
    public const string DisplayName = "MiniMax H3 (768P)";
    public const string LiteModelKey = "minimax_h3_lite";
    public const string LiteModelName = "MiniMax-H3-Lite";
    public const decimal LitePricePerSecondUsd = 0.04m;

    public static bool IsSupported(string? key) => key is null or ModelKey or LiteModelKey;

    /// <summary>Both Economy and Premium receive the application's structured I2VA preparation.</summary>
    public static bool UsesStructuredPrompt(string? key) => key is ModelKey or LiteModelKey;
}

/// <summary>
/// Immutable identity of the previous Veo strategy. Historical snapshots must keep resolving to
/// this model after the system default changes.
/// </summary>
public static class VeoVideoModel
{
    public const string ProviderKey = "gemini";
    public const string ModelKey = "veo_3_1_lite";
    public const string ModelName = "veo-3.1-lite-generate-preview";
    public const string Resolution = "720P";
    public const decimal PricePerSecondUsd = 0.05m;
    public const string DisplayName = "Google Veo 3.1 Lite";
}
