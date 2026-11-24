using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Finds the garment in an approved image with the clothes model (SegFormer), for the opening crop of an AI scene
/// (<see cref="AiOpeningCrop"/>). An image is looked at once: the box is remembered by its content hash, so planning a
/// preview and then the run gives the same crop.
/// </summary>
internal sealed class SegFormerGarmentLocator(IClothesSegmenter clothes, IMediaStorage storage, IHttpClientFactory clients,
    IMemoryCache cache, ILogger<SegFormerGarmentLocator> logger) : IGarmentLocator
{
    private const int MaxSourceBytes = 20_000_000;

    /// <summary>The longest side of the grid the garment is read on; the model itself answers at 128 x 128.</summary>
    private const int GridLongSide = 256;

    /// <summary>How likely (0..255) a cell must be the garment to count.</summary>
    private const byte GarmentThreshold = 128;

    /// <summary>Less garment than this share of the image is noise, not a garment.</summary>
    private const double MinimumGarmentShare = 0.01;

    /// <summary>The share of garment cells left out on each side, so a stray patch does not stretch the box.</summary>
    private const double OutlierShare = 0.02;

    private static readonly TimeSpan Remembered = TimeSpan.FromHours(6);

    public async Task<Region?> LocateAsync(MockupImage image, WornGarment garment, CancellationToken cancellationToken)
    {
        if (!clothes.IsAvailable || string.IsNullOrWhiteSpace(image.ContentHash)) return null;
        var key = $"garment-box:{image.ContentHash}:{garment}";
        if (cache.TryGetValue(key, out Region? known)) return known;

        Region? box;
        try
        {
            var url = storage.SignRead(image.StorageKey, "image", image.StorageVersion);
            using var response = await clients.CreateClient("PrivateMedia")
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxSourceBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > MaxSourceBytes) return null;

            using var photo = Image.Load<Rgba32>(bytes);
            var scale = (double)GridLongSide / Math.Max(photo.Width, photo.Height);
            var width = Math.Max(1, (int)Math.Round(photo.Width * scale));
            var height = Math.Max(1, (int)Math.Round(photo.Height * scale));
            box = Box(clothes.Garment(photo, width, height, garment), width, height);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not remembered: a download that failed is tried again by the next planning.
            logger.LogWarning(ex, "The garment in image {Image} could not be found; its AI scene opens on the whole image.", image.Id);
            return null;
        }

        cache.Set(key, box, Remembered);
        return box;
    }

    /// <summary>The box (shares of the grid) around the garment cells of <paramref name="mask"/>, or null with too few.</summary>
    internal static Region? Box(byte[]? mask, int width, int height)
    {
        if (mask is null || width <= 0 || height <= 0 || mask.Length != width * height) return null;
        var columns = new List<int>();
        var rows = new List<int>();
        for (var index = 0; index < mask.Length; index++)
        {
            if (mask[index] < GarmentThreshold) continue;
            columns.Add(index % width);
            rows.Add(index / width);
        }
        if (columns.Count < MinimumGarmentShare * mask.Length) return null;

        columns.Sort();
        rows.Sort();
        var skip = (int)Math.Floor(columns.Count * OutlierShare);
        var (left, right) = (columns[skip], columns[columns.Count - 1 - skip]);
        var (top, bottom) = (rows[skip], rows[rows.Count - 1 - skip]);
        return new Region((double)left / width, (double)top / height,
            (double)(right + 1 - left) / width, (double)(bottom + 1 - top) / height);
    }
}
