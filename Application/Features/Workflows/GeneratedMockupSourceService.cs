using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

public sealed record ImportGeneratedMockupsResponse(int ImportedCount, int FailedCount, IReadOnlyList<MockupResponse> Mockups);

public interface IGeneratedMockupSourceService
{
    Task<Result<ImportGeneratedMockupsResponse>> ImportAsync(Guid productId, CancellationToken ct);
}

/// <summary>
/// Turns the mock-ups composited for a product into video sources.
/// </summary>
/// <remarks>
/// A composited mock-up is a Cloudinary transformation URL, not a stored file, while a video is
/// rendered from an immutable PNG whose hash is checked at every step. Importing stores a snapshot
/// of each mock-up and records its key, version, hash and size on the same row. The mock-up is left
/// pending, so it is approved at the Mockup Approval gate like an uploaded one.
/// </remarks>
public sealed class GeneratedMockupSourceService(ICurrentUser user, IRepository<Product> products,
    IRepository<MockupImage> assets, IUnitOfWork uow, IMediaStorage storage, IMockupAssetService mockups,
    ILogger<GeneratedMockupSourceService> logger) : IGeneratedMockupSourceService
{
    /// <summary>Each import is one upload, so a single request stays bounded; the rest follow on the next call.</summary>
    public const int MaximumPerCall = 24;

    public async Task<Result<ImportGeneratedMockupsResponse>> ImportAsync(Guid productId, CancellationToken ct)
    {
        if (await products.GetByIdAsync(productId, ct) is not { DeletedAt: null } product || product.UserId != user.UserId)
            return Result.Failure<ImportGeneratedMockupsResponse>(Error.NotFound("MockupNotFound", "Product or mockup not found."));

        var waiting = (await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null &&
                x.SourceType == MockupSources.Generated && x.ContentHash == null && x.MockupImageUrl != null, ct))
            .OrderByDescending(x => x.CreatedAt).Take(MaximumPerCall).ToArray();

        var imported = 0;
        var failed = 0;
        foreach (var mockup in waiting)
        {
            if (!IsCompositeUrl(mockup.MockupImageUrl!))
            {
                failed++;
                continue;
            }
            try
            {
                var saved = await storage.UploadMockupFromUrlAsync(mockup.MockupImageUrl!, $"mockups/{user.UserId}/{productId}/{mockup.Id}", ct);
                mockup.StorageKey = saved.StorageKey;
                mockup.StorageVersion = saved.Version;
                mockup.ContentHash = saved.Hash;
                mockup.MockupWidthPx = saved.Width;
                mockup.MockupHeightPx = saved.Height;
                // Rows composited before these columns existed have neither.
                mockup.ArtworkGroupKey ??= mockup.DesignImageId is { } designImageId ? MockupSources.ArtworkGroupKey(designImageId) : productId.ToString();
                mockup.VariantKey ??= mockup.GarmentColor;
                imported++;
            }
            catch (Exception error) when (error is InvalidOperationException or HttpRequestException)
            {
                failed++;
                logger.LogWarning(error, "Could not store mock-up {MockupId} as a video source", mockup.Id);
            }
        }
        if (imported > 0) await uow.SaveChangesAsync(ct);

        var listed = await mockups.ListAsync(productId, ct);
        return listed.IsFailure
            ? Result.Failure<ImportGeneratedMockupsResponse>(listed.Error)
            : Result.Success(new ImportGeneratedMockupsResponse(imported, failed, listed.Value));
    }

    // Only a composite this application built is fetched: never an arbitrary address stored on a row.
    private static bool IsCompositeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "res.cloudinary.com";
}
