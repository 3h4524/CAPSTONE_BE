using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-3.5-flash";

    [Range(0, 2)]
    public float Temperature { get; set; } = 0.3f;

    [Range(256, 8192)]
    public int MaxOutputTokens { get; set; } = 2048;

    public string ThinkingLevel { get; set; } = "Low";

    [Range(0, 5)]
    public int RateLimitRetries { get; set; } = 2;

    [Range(0, 60)]
    public int RateLimitRetryDelaySeconds { get; set; } = 8;

    [Range(0, 30)]
    public int OverloadRetryDelaySeconds { get; set; } = 2;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
