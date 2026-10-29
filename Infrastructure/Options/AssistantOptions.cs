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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
