namespace APCS.Application.Abstractions.Storage;

/// <summary>Stores publicly readable images such as style preset previews.</summary>
public interface IPublicImageService
{
    /// <summary>Uploads a public image, replacing any image under the same storage key.</summary>
    /// <param name="file">The image to store.</param>
    /// <param name="storageKey">The stable key identifying the image.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The public delivery URL of the stored image.</returns>
    Task<string> UploadImageAsync(
        UploadFileDto file,
        string storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a previously uploaded public image. Missing images are ignored.</summary>
    /// <param name="storageKey">The stable key identifying the image.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeleteImageAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a public image the same way as <see cref="UploadImageAsync"/>, but also returns the
    /// pixel dimensions the storage provider measured — so callers (e.g. mock-up template creation)
    /// don't need a local image-processing library just to read a width/height.
    /// </summary>
    Task<PublicImageUploadResult> UploadImageWithMetadataAsync(
        UploadFileDto file,
        string storageKey,
        CancellationToken cancellationToken = default);
}

/// <summary>The delivery URL of a stored image plus its measured pixel dimensions.</summary>
public sealed record PublicImageUploadResult(string Url, int WidthPx, int HeightPx);
