using APCS.Application.Abstractions.Storage;

namespace APCS.IntegrationTests.TestSupport.Fakes;

/// <summary>
/// Keeps public image uploads in memory instead of sending them to Cloudinary.
/// </summary>
public sealed class FakePublicImageService : IPublicImageService
{
    private readonly Dictionary<string, UploadFileDto> _stored = new(StringComparer.Ordinal);

    /// <summary>Gets the storage keys currently held by the fake.</summary>
    public IReadOnlyCollection<string> StorageKeys => _stored.Keys;

    /// <inheritdoc />
    public Task<string> UploadImageAsync(
        UploadFileDto file,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        _stored[storageKey] = file;

        return Task.FromResult($"https://storage.test/{storageKey}");
    }

    /// <inheritdoc />
    public Task DeleteImageAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _stored.Remove(storageKey);
        return Task.CompletedTask;
    }
}
