using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Identifies the batch and optional product subset to inspect before export.</summary>
public sealed record BatchExportPreviewRequest(Guid BatchId, IReadOnlyList<Guid>? ProductIds = null);
/// <summary>One approved image that may be selected for export.</summary>
public sealed record BatchExportImage(Guid Id, string PreviewUrl, string? Format, int Width, int Height, long? Revision = null);
/// <summary>One approved completed video and whether its scene sources are safely recoverable.</summary>
public sealed record BatchExportVideo(Guid Id, int Version, string AspectRatio, string PreviewUrl,
    DateTimeOffset? CreatedAt, bool SceneExportAvailable);
/// <summary>Exportable content currently available for one product.</summary>
public sealed record BatchExportProduct(Guid ProductId, string Name, string ProductType,
    IReadOnlyList<BatchExportImage> Designs, IReadOnlyList<BatchExportImage> Mockups,
    IReadOnlyList<BatchExportVideo> Videos, ListingContentResponseDto? Listing, IReadOnlyList<string> Warnings);
/// <summary>Ownership-checked export candidates for a batch.</summary>
public sealed record BatchExportPreview(Guid BatchId, string BatchName, IReadOnlyList<BatchExportProduct> Products);

public interface IBatchExportPreviewService
{
    Task<Result<BatchExportPreview>> PreviewAsync(BatchExportPreviewRequest request, CancellationToken cancellationToken);
}

public sealed class BatchExportPreviewService(ICurrentUser user, IRepository<Batch> batches, IRepository<Product> products,
    IRepository<DesignImage> designs, IRepository<MockupImage> mockups, IRepository<PromoVideo> videos,
    IListingContentService listings, IMediaStorage storage) : IBatchExportPreviewService
{
    public async Task<Result<BatchExportPreview>> PreviewAsync(BatchExportPreviewRequest request, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || user.UserId is not Guid owner)
            return Result.Failure<BatchExportPreview>(Error.Unauthorized("Export.Unauthenticated", "Please sign in to export products."));
        if (request.BatchId == Guid.Empty || request.ProductIds is { Count: > 100 } ||
            request.ProductIds?.Any(id => id == Guid.Empty) == true ||
            request.ProductIds?.Distinct().Count() != request.ProductIds?.Count)
            return Result.Failure<BatchExportPreview>(Error.Validation("Select up to 100 distinct products in a batch."));
        var batch = await batches.GetByIdAsync(request.BatchId, cancellationToken);
        if (batch is null || batch.UserId != owner || batch.DeletedAt != null)
            return Result.Failure<BatchExportPreview>(Error.NotFound("Export.BatchNotFound", "Batch not found."));
        var owned = await products.FindAsync(product => product.BatchId == request.BatchId && product.UserId == owner && product.DeletedAt == null, cancellationToken);
        if (request.ProductIds is { Count: > 0 } && request.ProductIds.Any(id => owned.All(product => product.Id != id)))
            return Result.Failure<BatchExportPreview>(Error.NotFound("Export.ProductNotFound", "A selected product was not found in this batch."));
        var selected = owned.Where(product => request.ProductIds is not { Count: > 0 } || request.ProductIds.Contains(product.Id))
            .OrderBy(product => product.CreatedAt).ThenBy(product => product.Id).ToArray();
        if (selected.Length > 100)
            return Result.Failure<BatchExportPreview>(Error.Validation("Select up to 100 products at a time."));
        var ids = selected.Select(product => product.Id).ToArray();
        var designRows = await designs.FindAsync(item => ids.Contains(item.ProductId) && item.DeletedAt == null && item.ApprovalStatus == "approved", cancellationToken);
        var mockupRows = await mockups.FindAsync(item => ids.Contains(item.ProductId) && item.DeletedAt == null && item.ApprovalStatus == "approved" && item.ApprovedRevision == item.MetadataRevision, cancellationToken);
        var videoRows = await videos.FindAsync(item => ids.Contains(item.ProductId) && item.DeletedAt == null && item.Status == "completed" && item.ApprovalStatus == "approved", cancellationToken);
        var result = new List<BatchExportProduct>();
        foreach (var product in selected)
        {
            var designItems = designRows.Where(item => item.ProductId == product.Id && !string.IsNullOrWhiteSpace(item.StorageKey))
                .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
                .Select(item => new BatchExportImage(item.Id, storage.SignRead(item.StorageKey, "image"), item.FileFormat, item.ImageWidthPx, item.ImageHeightPx)).ToArray();
            var mockupItems = mockupRows.Where(item => item.ProductId == product.Id && !string.IsNullOrWhiteSpace(item.StorageKey) && !string.IsNullOrWhiteSpace(item.ContentHash))
                .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
                .Select(item => new BatchExportImage(item.Id, storage.SignRead(item.StorageKey, "image", item.StorageVersion), null, item.MockupWidthPx, item.MockupHeightPx, item.MetadataRevision)).ToArray();
            var videoItems = videoRows.Where(item => item.ProductId == product.Id && !string.IsNullOrWhiteSpace(item.StorageKey) && !string.IsNullOrWhiteSpace(item.StorageVersion))
                .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
                .Select(item => new BatchExportVideo(item.Id, item.VersionNumber, item.AspectRatio,
                    storage.SignRead(item.StorageKey!, "video", item.StorageVersion), item.CreatedAt is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : null,
                    SceneSourcesAvailable(item))).ToArray();
            var listingResult = await listings.GetAsync(product.Id, cancellationToken);
            if (listingResult.IsFailure && listingResult.Error.Code != "ListingContent.NotFound")
                return Result.Failure<BatchExportPreview>(listingResult.Error);
            var listing = listingResult.IsSuccess ? listingResult.Value : null;
            var warnings = new List<string>();
            if (designItems.Length == 0) warnings.Add("No approved design.");
            if (mockupItems.Length == 0) warnings.Add("No approved mockup.");
            if (videoItems.Length == 0) warnings.Add("No approved completed video.");
            if (listing is null) warnings.Add("No listing content.");
            result.Add(new(product.Id, product.Name, product.ProductType, designItems, mockupItems, videoItems, listing, warnings));
        }
        return Result.Success(new BatchExportPreview(batch.Id, batch.Name, result));
    }

    internal static bool SceneSourcesAvailable(PromoVideo video)
    {
        try
        {
            using var document = JsonDocument.Parse(video.ConfigSnapshot);
            if (!document.RootElement.TryGetProperty("exportSources", out var value) ||
                !document.RootElement.TryGetProperty("storyboard", out var boardValue)) return false;
            var snapshot = value.Deserialize<VideoExportSourceSnapshot>(WorkflowJson.Options);
            var board = boardValue.Deserialize<Storyboard>(WorkflowJson.Options);
            if (snapshot is null || board is null || snapshot.Scenes is null || board.Scenes is null || snapshot.VideoId != video.Id || snapshot.Version != 1 ||
                snapshot.StoryboardFingerprint != board.Fingerprint || video.Fingerprint != board.Fingerprint ||
                snapshot.Scenes.Count != board.Scenes.Count) return false;
            return board.Scenes.All(scene =>
            {
                var source = snapshot.Scenes.SingleOrDefault(item => item.SceneOrder == scene.SceneOrder);
                return source != null && source.MockupId == scene.MockupId && source.SourceHash == scene.SourceHash &&
                    source.SourceRevision == scene.SourceRevision && !string.IsNullOrWhiteSpace(source.ImageStorageKey) &&
                    !string.IsNullOrWhiteSpace(source.ImageStorageVersion) &&
                    (scene.GenerationStrategy != "ai_shot" ||
                        !string.IsNullOrWhiteSpace(source.ClipFingerprint) && !string.IsNullOrWhiteSpace(source.ClipStorageKey) && !string.IsNullOrWhiteSpace(source.ClipStorageVersion) &&
                        board.AiShots?.Any(shot => shot.SceneOrder == scene.SceneOrder && shot.Fingerprint == source.ClipFingerprint) == true);
            });
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentNullException) { return false; }
    }
}
