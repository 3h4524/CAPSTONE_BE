namespace APCS.Application.Abstractions.Ai;

public sealed record GeminiTextGenerationRequest(string Prompt, string? SystemInstruction = null);

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
