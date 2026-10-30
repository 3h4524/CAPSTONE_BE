namespace APCS.Application.Abstractions.AI;

/// <summary>Generates one design image per call from a text prompt, using the Seller's own BYOK key.</summary>
public interface IImageGenerationProvider
{
    /// <summary>The provider name its Seller's API key and usage records are stored under (see <see cref="ImageGenerationProviders"/>).</summary>
    string Provider { get; }

    /// <summary>
    /// <see langword="true"/> when the provider can draw the artwork on a transparent background itself, so the
    /// caller does not have to ask for a plain backdrop and cut it out afterwards.
    /// </summary>
    bool SupportsTransparentBackground { get; }

    /// <summary>Estimated cost of one generated image with the currently configured model.</summary>
    decimal EstimatedCostPerImageUsd { get; }

    /// <summary>
    /// Requests one generated image. The call is synchronous: the provider either returns the image
    /// bytes directly or reports why it could not (content-safety block, or a provider error).
    /// </summary>
    /// <param name="apiKey">The Seller's decrypted provider API key.</param>
    /// <param name="prompt">The fully synthesized <c>AiPrompt.GeneratedPrompt</c>.</param>
    /// <param name="aspectRatio">The requested aspect ratio (e.g. "1:1", "16:9").</param>
    /// <param name="transparentBackground">
    /// Asks for the artwork alone on a transparent background. Ignored by a provider whose
    /// <see cref="SupportsTransparentBackground"/> is <see langword="false"/>.
    /// </param>
    /// <param name="enrichPrompt">
    /// Lets the provider fill out a prompt that says little (Ideogram's magic prompt), for a product the Seller gave
    /// next to nothing to design from. Ignored by a provider that has no such step.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<ImageGenerationResult> GenerateAsync(
        string apiKey,
        string prompt,
        string aspectRatio,
        bool transparentBackground = false,
        bool enrichPrompt = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests one image generated from a text instruction and reference images, in the order given.
    /// </summary>
    /// <param name="apiKey">The Seller's decrypted provider API key.</param>
    /// <param name="prompt">What to do with the reference images.</param>
    /// <param name="images">The reference images the prompt refers to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<ImageGenerationResult> ComposeAsync(
        string apiKey,
        string prompt,
        IReadOnlyList<ImageReference> images,
        CancellationToken cancellationToken = default);
}

/// <summary>An image sent to the provider together with a prompt.</summary>
public sealed record ImageReference(byte[] Bytes, string MimeType);

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

    /// <summary>
    /// <see langword="true"/> when the provider turned the account away (no credits, a key it no longer accepts, a
    /// daily limit): every other image of the job would be refused the same way, so the job stops asking.
    /// </summary>
    public bool StopsJob { get; init; }

    /// <summary>How long the provider took to make the image, when it reports it apart from <see cref="DownloadTime"/>.</summary>
    public TimeSpan? GenerateTime { get; init; }

    /// <summary>How long the finished image took to download, for a provider that answers with a link.</summary>
    public TimeSpan? DownloadTime { get; init; }

    /// <summary>The provider's random seed for the image, to make it again.</summary>
    public long? Seed { get; init; }

    /// <summary>The prompt the provider actually drew from, when it rewrites the one it was sent.</summary>
    public string? ProviderPrompt { get; init; }

    public static ImageGenerationResult Success(byte[] imageBytes, string mimeType, string model, decimal estimatedCostUsd) =>
        new(imageBytes, mimeType, false, null, model, estimatedCostUsd);

    public static ImageGenerationResult Blocked(string reason, string model) =>
        new(null, null, true, reason, model, 0);

    public static ImageGenerationResult Failed(string reason, string model) =>
        new(null, null, false, reason, model, 0);

    /// <summary>A failure that every further request of the job would meet too (see <see cref="StopsJob"/>).</summary>
    public static ImageGenerationResult AccountRefused(string reason, string model) =>
        new(null, null, false, reason, model, 0) { StopsJob = true };
}
