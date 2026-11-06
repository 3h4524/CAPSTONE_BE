namespace APCS.Application.Abstractions.Ai;

public sealed record GeminiInputImage(byte[] Bytes, string MimeType);

public sealed record GeminiTextGenerationRequest(string Prompt, string? SystemInstruction = null,
    IReadOnlyList<GeminiInputImage>? Images = null, decimal? Temperature = null,
    object? ResponseSchema = null, string? Model = null, string? FallbackModel = null,
    TimeSpan? AttemptTimeout = null);

public sealed record GeminiTextGenerationResult(
    string Text,
    string Model,
    int? InputTokens,
    int? OutputTokens,
    string? ProviderRequestId);

public interface IGeminiTextGenerationProvider
{
    /// <summary>Whether an API key is configured, so a caller can skip work that needs one.</summary>
    bool IsConfigured { get; }

    Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default);
}
