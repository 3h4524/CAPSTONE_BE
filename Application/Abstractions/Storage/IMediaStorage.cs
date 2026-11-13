using APCS.Application.Features.Workflows;

namespace APCS.Application.Abstractions.Storage;

public sealed record StoredMockup(string StorageKey, string Version, string Hash, int Width, int Height);
public interface IMediaStorage
{
    Task<StoredMockup> UploadMockupAsync(Stream content, string fileName, string storageKey, CancellationToken cancellationToken);
    /// <summary>Stores the image at <paramref name="sourceUrl"/> the same way as an uploaded mock-up.</summary>
    Task<StoredMockup> UploadMockupFromUrlAsync(string sourceUrl, string storageKey, CancellationToken cancellationToken);
    /// <summary>Reads a stored mock-up image back as PNG bytes.</summary>
    Task<byte[]> ReadImageAsync(string storageKey, string? version, CancellationToken cancellationToken);
    /// <summary>Reads a publicly stored image (a generated design) back as PNG bytes.</summary>
    Task<byte[]> ReadPublicImageAsync(string storageKey, CancellationToken cancellationToken);
    /// <summary>Reads a mock-up composited on the fly (a Cloudinary delivery URL of this account), as JPEG or PNG bytes.</summary>
    /// <exception cref="InvalidOperationException">The URL is not one of this account's Cloudinary images.</exception>
    Task<byte[]> ReadCompositeAsync(string url, CancellationToken cancellationToken);
    string SignRead(string storageKey, string resourceType, string? version = null, bool attachment = false);

    /// <summary>
    /// A signed link to a stored image padded to exactly <paramref name="width"/> × <paramref name="height"/> (the whole
    /// image kept, the rest filled with its own edge color), the size a social platform shows. A storage that cannot
    /// resize gives the image as it is.
    /// </summary>
    string SignReadFitted(string storageKey, string? version, int width, int height, bool attachment = false) =>
        SignRead(storageKey, "image", version, attachment);
    UploadGrant CreateUploadGrant(string storageKey, string resourceType);
    Task<bool> VerifyAsync(string storageKey, string resourceType, string version, long? bytes, CancellationToken cancellationToken);
}
