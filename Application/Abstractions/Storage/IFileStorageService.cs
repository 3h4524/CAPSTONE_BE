namespace APCS.Application.Abstractions.Storage;

/// <summary>
/// Stores private application files and creates short-lived authorized download links.
/// </summary>
public interface IFileStorageService
{
    /// <summary>Uploads a private file under a deterministic storage key.</summary>
    Task<StoredFileDto> UploadAsync(
        UploadFileDto file,
        string storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a previously uploaded file.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Creates a time-limited download URL for a private file.</summary>
    SignedDownloadDto CreateSignedDownload(string storageKey, string fileName);
}
