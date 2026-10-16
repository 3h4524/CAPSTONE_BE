namespace APCS.Application.Features.Workflows;

/// <summary>Central model selection for AI video so plans, provider routing and quotes stay aligned.</summary>
public static class AiVideoModel
{
    public const string ProviderKey = "gemini";
    public const string ModelKey = "veo_3_1_lite";
    public const string ModelName = "veo-3.1-lite-generate-preview";
    public const decimal PricePerSecondUsd = 0.05m;
    public const string DisplayName = "Google Veo 3.1 Lite";
}
