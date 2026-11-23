namespace APCS.Application.Abstractions.AI;

/// <summary>
/// A provider that keeps the finished image behind a link, so the image can be shown and stored without first passing
/// through this server.
/// </summary>
public interface IImageLinkProvider
{
    /// <summary>
    /// Makes one image and stops at its link: <see cref="ImageGenerationResult.ImageUrl"/> is set and
    /// <see cref="ImageGenerationResult.ImageBytes"/> is not. The link is temporary.
    /// </summary>
    Task<ImageGenerationResult> GenerateLinkAsync(
        string apiKey,
        string prompt,
        string aspectRatio,
        bool transparentBackground = false,
        bool enrichPrompt = false,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the image behind a link from <see cref="GenerateLinkAsync"/>, for when storage could not fetch it itself.</summary>
    Task<ImageGenerationResult> DownloadLinkAsync(
        ImageGenerationResult linked,
        CancellationToken cancellationToken = default);
}
