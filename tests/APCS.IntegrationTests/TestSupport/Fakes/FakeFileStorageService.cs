using APCS.Application.Abstractions.Storage;

namespace APCS.IntegrationTests.TestSupport.Fakes;

/// <summary>
/// Keeps private uploads in memory instead of sending them to Cloudinary.
/// </summary>
public sealed class FakeFileStorageService : IFileStorageService
{
    private readonly Dictionary<string, UploadFileDto> _stored = new(StringComparer.Ordinal);

    /// <summary>Gets the storage keys currently held by the fake.</summary>
    public IReadOnlyCollection<string> StorageKeys => _stored.Keys;

    /// <inheritdoc />
    public Task<StoredFileDto> UploadAsync(
        UploadFileDto file,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        _stored[storageKey] = file;

        return Task.FromResult(new StoredFileDto(storageKey, $"https://storage.test/{storageKey}"));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _stored.Remove(storageKey);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public SignedDownloadDto CreateSignedDownload(string storageKey, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        return new SignedDownloadDto(
            $"https://storage.test/{storageKey}",
            DateTimeOffset.UtcNow.AddMinutes(10));
    }
}
