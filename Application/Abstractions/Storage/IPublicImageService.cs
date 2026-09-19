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
}
