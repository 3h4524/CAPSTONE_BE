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
    ILogger<GeneratedMockupSourceService> logger, TimeProvider time) : IGeneratedMockupSourceService
{
    /// <summary>Each import is one upload, so a single request stays bounded; the rest follow on the next call.</summary>
    public const int MaximumPerCall = 24;

    /// <summary>
    /// Uploads in flight at once. Each one is a render, a store and a read-back at the image host, so it waits on the
    /// network rather than on this process. Twelve put every mock-up of a usual product (two to four templates in three
    /// colors) in one round, so a product waits about one upload's time; the cap stops a call of up to
    /// <see cref="MaximumPerCall"/> from asking the image host to render everything at once.
    /// </summary>
    public const int UploadConcurrency = 12;

    /// <summary>The wait before the one retry of an upload that failed for a passing reason (busy host, timeout, dropped connection).</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task<Result<ImportGeneratedMockupsResponse>> ImportAsync(Guid productId, CancellationToken ct)
    {
        if (await products.GetByIdAsync(productId, ct) is not { DeletedAt: null } product || product.UserId != user.UserId)
            return Result.Failure<ImportGeneratedMockupsResponse>(Error.NotFound("MockupNotFound", "Product or mockup not found."));

        var waiting = (await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null &&
                x.SourceType == MockupSources.Generated && x.ContentHash == null && x.MockupImageUrl != null, ct))
            .OrderByDescending(x => x.CreatedAt).Take(MaximumPerCall).ToArray();

        // The same composite already among the video sources, or twice in this list, is added once: the copy waiting is
        // removed. Only a mock-up made exactly the same way counts; one that merely looks alike is kept.
        var present = (await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null &&
                x.SourceType == MockupSources.Generated && x.ContentHash != null && x.MockupImageUrl != null, ct))
            .Select(SameMockup).ToHashSet();
        var imported = 0;
        var failed = 0;
        var duplicates = 0;
        var toStore = new List<MockupImage>(waiting.Length);
        foreach (var mockup in waiting)
        {
            if (!present.Add(SameMockup(mockup)))
            {
                mockup.DeletedAt = time.GetUtcNow().UtcDateTime;
                duplicates++;
                continue;
            }
            if (!IsCompositeUrl(mockup.MockupImageUrl!))
            {
                failed++;
                continue;
            }
            toStore.Add(mockup);
        }

        // Only the uploads run side by side: they talk to the image host and never touch the database context, which is
        // not safe to share between threads. What each one returned is written to its row below, one at a time.
        var stored = new StoredMockup?[toStore.Count];
        var seconds = new double[toStore.Count];
        var started = time.GetTimestamp();
        using var gate = new SemaphoreSlim(UploadConcurrency);
        await Task.WhenAll(toStore.Select(async (mockup, index) =>
        {
            await gate.WaitAsync(ct);
            var uploadStarted = time.GetTimestamp();
            try
            {
                stored[index] = await StoreAsync(mockup, productId, ct);
                if (stored[index] is not null) seconds[index] = time.GetElapsedTime(uploadStarted).TotalSeconds;
            }
            finally
            {
                gate.Release();
            }
        }));

        for (var index = 0; index < toStore.Count; index++)
        {
            if (stored[index] is not { } saved)
            {
                failed++;
                continue;
            }
            var mockup = toStore[index];
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
        if (toStore.Count > 0) LogTimes(productId, toStore.Count, imported, time.GetElapsedTime(started), seconds, stored);
        if (imported > 0 || duplicates > 0) await uow.SaveChangesAsync(ct);

        var listed = await mockups.ListAsync(productId, ct);
        return listed.IsFailure
            ? Result.Failure<ImportGeneratedMockupsResponse>(listed.Error)
            : Result.Success(new ImportGeneratedMockupsResponse(imported, failed, listed.Value));
    }

    // One line per import: the whole wait, then what each mock-up spent at the image host (render and store) and reading
    // its file back for the hash, so the slow part shows.
    private void LogTimes(Guid productId, int count, int imported, TimeSpan total, double[] seconds, StoredMockup?[] stored)
    {
        var done = stored.OfType<StoredMockup>().ToArray();
        static (double Min, double Max) Range(IEnumerable<double> values)
        {
            var all = values.DefaultIfEmpty().ToArray();
            return (all.Min(), all.Max());
        }
        var each = Range(seconds.Where(x => x > 0));
        var store = Range(done.Select(x => x.StoreTime?.TotalSeconds ?? 0));
        var readBack = Range(done.Select(x => x.ReadBackTime?.TotalSeconds ?? 0));
        var megabytes = done.Length == 0 ? 0 : done.Average(x => x.Bytes ?? 0) / (1024d * 1024d);
        logger.LogInformation(
            "Stored {Imported} of {Count} mock-ups of product {ProductId} in {Seconds:0.0}s, {Concurrency} at a time: each {Fastest:0.0}-{Slowest:0.0}s "
            + "(render and store at Cloudinary {StoreFastest:0.0}-{StoreSlowest:0.0}s, read back {ReadFastest:0.0}-{ReadSlowest:0.0}s, {MegaBytes:0.0} MB on average)",
            imported, count, productId, total.TotalSeconds, UploadConcurrency, each.Min, each.Max, store.Min, store.Max, readBack.Min, readBack.Max, megabytes);
    }

    // Many uploads at once can find the image host busy or slow; one that failed for such a reason is tried once more,
    // so a mock-up is not left out of Mockup Approval over a passing hiccup. An image the host refused is not retried.
    private async Task<StoredMockup?> StoreAsync(MockupImage mockup, Guid productId, CancellationToken ct)
    {
        var key = $"mockups/{user.UserId}/{productId}/{mockup.Id}";
        try
        {
            return await storage.UploadMockupFromUrlAsync(mockup.MockupImageUrl!, key, ct);
        }
        catch (Exception error) when (IsPassing(error, ct))
        {
            logger.LogInformation(error, "Storing mock-up {MockupId} failed for a passing reason; trying once more", mockup.Id);
        }
        catch (InvalidOperationException error)
        {
            logger.LogWarning(error, "Could not store mock-up {MockupId} as a video source", mockup.Id);
            return null;
        }

        try
        {
            await Task.Delay(RetryDelay, time, ct);
            return await storage.UploadMockupFromUrlAsync(mockup.MockupImageUrl!, key, ct);
        }
        catch (Exception error) when (error is InvalidOperationException || IsPassing(error, ct))
        {
            logger.LogWarning(error, "Could not store mock-up {MockupId} as a video source", mockup.Id);
            return null;
        }
    }

    // A dropped connection, a busy answer or a timeout; never the caller cancelling.
    private static bool IsPassing(Exception error, CancellationToken ct) =>
        !ct.IsCancellationRequested && error is HttpRequestException or TaskCanceledException or TimeoutException;

    private static (Guid?, Guid?, string?) SameMockup(MockupImage mockup) => (mockup.DesignImageId, mockup.MockupTemplateId, mockup.MockupImageUrl);

    // Only a composite this application built is fetched: never an arbitrary address stored on a row.
    private static bool IsCompositeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "res.cloudinary.com";
}
