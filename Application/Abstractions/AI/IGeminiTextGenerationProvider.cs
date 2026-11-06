namespace APCS.Application.Abstractions.Ai;

public sealed record GeminiInputImage(byte[] Bytes, string MimeType);

public sealed record GeminiTextGenerationRequest(string Prompt, string? SystemInstruction = null,
    IReadOnlyList<GeminiInputImage>? Images = null, decimal? Temperature = null);

public sealed record GeminiTextGenerationResult(
    string Text,
    string Model,
    int? InputTokens,
    int? OutputTokens,
    string? ProviderRequestId);

public interface IGeminiTextGenerationProvider
{
    Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default);
}
