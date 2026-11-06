using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Resolves only the product image explicitly recorded when a studio frame was generated.</summary>
internal static class VideoQualityReferenceMockup
{
    public static async Task<byte[]?> ReadAsync(MockupImage frame, IRepository<MockupImage> assets,
        IMediaStorage storage, CancellationToken cancellationToken)
    {
        var metadata = AiStudioFrameIdentity.Read(frame.Regions);
        if (metadata is null || metadata.SourceMockupId == frame.Id) return null;
        var original = await assets.GetByIdAsync(metadata.SourceMockupId, cancellationToken);
        if (original is null || original.ProductId != frame.ProductId || original.DeletedAt is not null ||
            original.MetadataRevision != metadata.SourceRevision || original.ContentHash != metadata.SourceHash ||
            string.IsNullOrWhiteSpace(original.StorageKey)) return null;
        try
        {
            return await storage.ReadImageAsync(original.StorageKey, original.StorageVersion, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Reference is optional; the scene itself can still be evaluated.
            return null;
        }
    }
}
