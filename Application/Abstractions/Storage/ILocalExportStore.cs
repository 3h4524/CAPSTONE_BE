namespace APCS.Application.Abstractions.Storage;

public sealed record LocalExportFile(string Key, string Sha256, long Bytes);

/// <summary>Stores ZIP exports on the API host so package delivery does not depend on Cloudinary.</summary>
public interface ILocalExportStore
{
    Task<LocalExportFile> SaveAsync(Guid jobId, Guid leaseToken, Stream content, CancellationToken ct);
    Task<bool> VerifyAsync(string key, string sha256, long? bytes, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
}
