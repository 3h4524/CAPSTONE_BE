namespace APCS.Application.Abstractions.AI;

/// <summary>Generates one design image per call from a text prompt, using the Seller's own BYOK key.</summary>
public interface IImageGenerationProvider
{
    /// <summary>
    /// Requests one generated image. The call is synchronous: the provider either returns the image
    /// bytes directly or reports why it could not (content-safety block, or a provider error).
    /// </summary>
    /// <param name="apiKey">The Seller's decrypted provider API key.</param>
    /// <param name="prompt">The fully synthesized <c>AiPrompt.GeneratedPrompt</c>.</param>
    /// <param name="aspectRatio">The requested aspect ratio (e.g. "1:1", "16:9").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<ImageGenerationResult> GenerateAsync(
        string apiKey,
        string prompt,
        string aspectRatio,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of one image generation call.
/// </summary>
/// <param name="ImageBytes">The generated image bytes, or <see langword="null"/> when generation did not succeed.</param>
/// <param name="MimeType">The image MIME type (e.g. "image/png"), present only when <paramref name="ImageBytes"/> is present.</param>
/// <param name="IsBlocked">
/// <see langword="true"/> when the provider refused to generate an image for content-safety reasons
/// rather than a transient/provider error — the caller should mark the product Failed with a clear
/// reason instead of retrying.
/// </param>
/// <param name="ErrorMessage">A human-readable reason when generation failed or was blocked.</param>
/// <param name="ModelUsed">
/// The provider's own model identifier, so callers (Application layer) can record it without
/// depending on the Infrastructure-layer options type that configures it.
/// </param>
/// <param name="EstimatedCostUsd">The provider's own per-call cost estimate, for the same reason.</param>
public sealed record ImageGenerationResult(
    byte[]? ImageBytes,
    string? MimeType,
    bool IsBlocked,
    string? ErrorMessage,
    string ModelUsed,
    decimal EstimatedCostUsd)
{
    public bool IsSuccess => ImageBytes is not null;

    public static ImageGenerationResult Success(byte[] imageBytes, string mimeType, string model, decimal estimatedCostUsd) =>
        new(imageBytes, mimeType, false, null, model, estimatedCostUsd);

    public static ImageGenerationResult Blocked(string reason, string model) =>
        new(null, null, true, reason, model, 0);

    public static ImageGenerationResult Failed(string reason, string model) =>
        new(null, null, false, reason, model, 0);
}
