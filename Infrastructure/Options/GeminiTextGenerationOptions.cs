using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class GeminiTextGenerationOptions
{
    public const string SectionName = "Gemini:TextGeneration";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-3.5-flash";

    /// <summary>
    /// Used when <see cref="Model"/> is overloaded or does not answer in time. Leave empty to only retry <see cref="Model"/>.
    /// </summary>
    public string FallbackModel { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>How long one model may take before the next attempt starts.</summary>
    [Range(5, 300)]
    public int AttemptTimeoutSeconds { get; set; } = 60;

    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    [Range(0, 2)]
    public decimal Temperature { get; set; } = 0.7m;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
