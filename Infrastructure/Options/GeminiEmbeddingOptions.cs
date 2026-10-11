using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class GeminiEmbeddingOptions
{
    public const string SectionName = "Gemini:Embeddings";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-embedding-001";

    [Range(128, 3072)]
    public int Dimensions { get; set; } = 1536;

    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
