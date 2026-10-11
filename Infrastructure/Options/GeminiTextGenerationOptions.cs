using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class GeminiTextGenerationOptions
{
    public const string SectionName = "Gemini:TextGeneration";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-3.8-flash";

    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    [Range(0, 2)]
    public decimal Temperature { get; set; } = 0.7m;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
