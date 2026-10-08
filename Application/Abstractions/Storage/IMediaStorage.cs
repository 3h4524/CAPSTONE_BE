using APCS.Application.Features.Workflows;

namespace APCS.Application.Abstractions.Storage;

public sealed record StoredMockup(string StorageKey, string Version, string Hash, int Width, int Height);
public interface IMediaStorage
{
    Task<StoredMockup> UploadMockupAsync(Stream content, string fileName, string storageKey, CancellationToken cancellationToken);
    /// <summary>Stores the image at <paramref name="sourceUrl"/> the same way as an uploaded mock-up.</summary>
    Task<StoredMockup> UploadMockupFromUrlAsync(string sourceUrl, string storageKey, CancellationToken cancellationToken);
    string SignRead(string storageKey, string resourceType, string? version = null, bool attachment = false);
    UploadGrant CreateUploadGrant(string storageKey, string resourceType);
    Task<bool> VerifyAsync(string storageKey, string resourceType, string version, long? bytes, CancellationToken cancellationToken);
}
