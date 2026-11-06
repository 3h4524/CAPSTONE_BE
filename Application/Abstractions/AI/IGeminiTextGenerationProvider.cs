namespace APCS.Application.Abstractions.Ai;

/// <param name="ResponseSchema">
/// The JSON schema Gemini must answer in (its <c>responseSchema</c>); null for any JSON.
/// </param>
/// <param name="Model">The model to use instead of the configured one; null for the configured one.</param>
/// <param name="FallbackModel">The model to retry on when <paramref name="Model"/> is overloaded; null for the configured fallback.</param>
/// <param name="AttemptTimeout">How long one attempt may take instead of the configured limit; null for the configured one.</param>
/// <param name="Temperature">The sampling temperature instead of the configured one; null for the configured one.</param>
public sealed record GeminiTextGenerationRequest(
    string Prompt,
    string? SystemInstruction = null,
    object? ResponseSchema = null,
    string? Model = null,
    string? FallbackModel = null,
    TimeSpan? AttemptTimeout = null,
    decimal? Temperature = null);

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
