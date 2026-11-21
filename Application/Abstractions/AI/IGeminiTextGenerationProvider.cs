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

public interface IGeminiTextStreamObserver
{
    /// <param name="restarted">True when an earlier attempt failed: the text received so far is void.</param>
    Task AttemptStartedAsync(string model, bool restarted, CancellationToken cancellationToken);

    Task TextReceivedAsync(string text, CancellationToken cancellationToken);
}

public interface IGeminiTextGenerationProvider
{
    /// <summary>Whether an API key is configured, so a caller can skip work that needs one.</summary>
    bool IsConfigured { get; }

    Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="GenerateAsync"/>, but streams the answer and hands each text chunk to <paramref name="observer"/>.
    /// </summary>
    Task<GeminiTextGenerationResult> GenerateStreamingAsync(
        GeminiTextGenerationRequest request,
        IGeminiTextStreamObserver observer,
        CancellationToken cancellationToken = default);
}
