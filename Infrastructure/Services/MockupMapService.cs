using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp.Processing;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Prepares mock-up base photos with <see cref="IMockupMapGenerator"/> and stores the derived
/// helper images next to the photo.
/// </summary>
public sealed class MockupMapService(
    IHttpClientFactory clients,
    IMockupMapGenerator generator,
    IPublicImageService images,
    IMemoryCache cache) : IMockupMapService
{
    internal const string HttpClientName = "MockupMaps";
    internal const int PreviewMaxSide = 800;

    // Measuring only needs the garment's outline, so the mask is fetched this small.
    internal const int MeasureMaxSide = 800;

    // Large enough to select a garment's edge where an arm crosses it; the selection is scaled up from it.
    internal const int ModelPreviewMaxSide = 1600;

    public bool IsAvailable => generator.IsAvailable;

    public Task<PreparedBasePhoto> PrepareAsync(byte[] photo, bool preview = false, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var garment = generator.GenerateGarmentMask(photo, preview ? PreviewMaxSide : null);
            var displacement = preview ? null : generator.GenerateDisplacementMap(photo);
            return new PreparedBasePhoto(
                garment.MaskPng, displacement,
                new GarmentMaskStats(garment.GarmentCoverage, garment.GarmentLuminance), garment.WidthPx, garment.HeightPx,
                preview ? null : generator.GenerateCutMask(garment.MaskPng));
        }, cancellationToken);

    // A model photo keeps the photo's own background (the person stands in it), so it has no cut mask.
    public Task<PreparedBasePhoto?> PrepareModelPhotoAsync(byte[] photo, byte[]? garmentSelection, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            // Found automatically at the photo's full size, not taken from the smaller preview.
            var selection = garmentSelection ?? generator.GenerateWornGarmentMask(photo);
            if (selection is null)
                return null;

            var garment = generator.GenerateGarmentMask(photo, garmentSelection: selection);
            var displacement = generator.GenerateDisplacementMap(photo, selection);
            return (PreparedBasePhoto?)new PreparedBasePhoto(
                garment.MaskPng, displacement,
                new GarmentMaskStats(garment.GarmentCoverage, garment.GarmentLuminance), garment.WidthPx, garment.HeightPx);
        }, cancellationToken);

    public Task<PreparedBasePhoto> PreviewModelPhotoAsync(byte[] photo, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var person = generator.GenerateGarmentMask(photo, ModelPreviewMaxSide);
            var worn = generator.GenerateWornGarmentMask(photo, ModelPreviewMaxSide);
            return new PreparedBasePhoto(
                person.MaskPng, null,
                new GarmentMaskStats(person.GarmentCoverage, person.GarmentLuminance), person.WidthPx, person.HeightPx,
                WornGarmentPng: worn, GarmentCount: worn is null ? null : CountGarments(worn));
        }, cancellationToken);

    // How many separate garments a mask holds: one per person standing apart.
    private static int CountGarments(byte[] maskPng)
    {
        using var mask = ImageMaskOps.LoadPhoto(maskPng);
        using var small = ImageMaskOps.ScaledCopy(mask, MeasureMaxSide);
        var alpha = ImageMaskOps.Pixels(small).Select(pixel => pixel.A).ToArray();
        return ImageMaskOps.MeasureGarments(alpha, small.Width, small.Height).Count;
    }

    // A mask's URL names its version, so what it measures never changes and is kept.
    public async Task<GarmentMeasurement?> MeasureGarmentAsync(string garmentMaskUrl, CancellationToken cancellationToken = default)
    {
        var key = $"garment-measurement:{garmentMaskUrl}";
        if (cache.TryGetValue(key, out Measured? known) && known is not null)
            return known.Value;

        var png = await clients.CreateClient(HttpClientName).GetByteArrayAsync(Limited(garmentMaskUrl, MeasureMaxSide), cancellationToken);
        var measured = await Task.Run(() =>
        {
            using var mask = ImageMaskOps.LoadPhoto(png);
            var alpha = ImageMaskOps.Pixels(mask).Select(pixel => pixel.A).ToArray();
            return ImageMaskOps.MeasureGarment(alpha, mask.Width, mask.Height);
        }, cancellationToken);
        cache.Set(key, new Measured(measured), TimeSpan.FromHours(12));
        return measured;
    }

    private sealed record Measured(GarmentMeasurement? Value);

    public async Task<IReadOnlyList<GarmentMeasurement>> MeasureGarmentsAsync(string garmentMaskUrl, CancellationToken cancellationToken = default)
    {
        var key = $"garment-measurements:{garmentMaskUrl}";
        if (cache.TryGetValue(key, out IReadOnlyList<GarmentMeasurement>? known) && known is not null)
            return known;

        var png = await clients.CreateClient(HttpClientName).GetByteArrayAsync(Limited(garmentMaskUrl, MeasureMaxSide), cancellationToken);
        var measured = await Task.Run(() =>
        {
            using var mask = ImageMaskOps.LoadPhoto(png);
            var alpha = ImageMaskOps.Pixels(mask).Select(pixel => pixel.A).ToArray();
            return ImageMaskOps.MeasureGarments(alpha, mask.Width, mask.Height);
        }, cancellationToken);
        cache.Set(key, measured, TimeSpan.FromHours(12));
        return measured;
    }

    // Both images name their version, so the color read from them never changes and is kept.
    public async Task<string?> MeasureGarmentColorAsync(string photoUrl, string garmentMaskUrl, CancellationToken cancellationToken = default)
    {
        var key = $"garment-color:{photoUrl}|{garmentMaskUrl}";
        if (cache.TryGetValue(key, out MeasuredColor? known) && known is not null)
            return known.Value;

        var client = clients.CreateClient(HttpClientName);
        var photo = await client.GetByteArrayAsync(Limited(AsJpeg(photoUrl), ColorMaxSide), cancellationToken);
        var mask = await client.GetByteArrayAsync(Limited(garmentMaskUrl, ColorMaxSide), cancellationToken);
        var color = await Task.Run(() =>
        {
            using var photoImage = ImageMaskOps.LoadPhoto(photo);
            using var maskImage = ImageMaskOps.LoadPhoto(mask);
            // The two come back scaled alike, give or take a pixel of rounding.
            if (maskImage.Width != photoImage.Width || maskImage.Height != photoImage.Height)
                maskImage.Mutate(c => c.Resize(photoImage.Width, photoImage.Height));
            return ImageMaskOps.GarmentColor(ImageMaskOps.Pixels(photoImage), ImageMaskOps.Pixels(maskImage).Select(pixel => pixel.A).ToArray());
        }, cancellationToken);
        cache.Set(key, new MeasuredColor(color), TimeSpan.FromHours(12));
        return color;
    }

    // A garment's color needs only a few thousand of its pixels.
    internal const int ColorMaxSide = 400;

    private sealed record MeasuredColor(string? Value);

    /// <summary>Asks the CDN for the image scaled down to fit <paramref name="maxSide"/>, keeping its format.</summary>
    internal static string Limited(string url, int maxSide)
    {
        const string marker = "/upload/";
        var index = url.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? url : url.Insert(index + marker.Length, $"c_limit,w_{maxSide},h_{maxSide}/");
    }

    public Task<byte[]> DownloadAsync(string baseImageUrl, CancellationToken cancellationToken = default) =>
        clients.CreateClient(HttpClientName).GetByteArrayAsync(AsJpeg(baseImageUrl), cancellationToken);

    // Quality high enough that the garment's edge and folds come out as in the original.
    internal const int PhotoJpegQuality = 92;

    public Task<byte[]> ToJpegAsync(byte[] photo, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var image = ImageMaskOps.LoadPhoto(photo);
            using var stream = new MemoryStream();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = PhotoJpegQuality });
            return stream.ToArray();
        }, cancellationToken);

    public async Task StoreHelpersAsync(string keyPrefix, PreparedBasePhoto prepared, CancellationToken cancellationToken = default)
    {
        if (prepared.DisplacementPng is { } displacement)
            await UploadAsync(displacement, MockupRules.DisplacementMapKey(keyPrefix), cancellationToken);
        await UploadAsync(prepared.MaskPng, MockupRules.GarmentMaskKey(keyPrefix), cancellationToken);
        if (prepared.CutMaskPng is { } cut)
            await UploadAsync(cut, MockupRules.CutMaskKey(keyPrefix), cancellationToken);
    }

    public async Task<bool> EnsureCutMaskAsync(string maskUrl, string cutMaskUrl, string cutMaskKey, CancellationToken cancellationToken = default)
    {
        var client = clients.CreateClient(HttpClientName);
        using (var existing = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, cutMaskUrl), cancellationToken))
        {
            if (existing.IsSuccessStatusCode)
                return true;
        }

        var mask = await client.GetByteArrayAsync(maskUrl, cancellationToken);
        var cut = await Task.Run(() => generator.GenerateCutMask(mask), cancellationToken);
        if (cut is null)
            return false;

        await UploadAsync(cut, cutMaskKey, cancellationToken);
        return true;
    }

    /// <summary>
    /// Asks the CDN for a JPEG rendition, so any uploaded format (including AVIF, which the image
    /// library cannot decode) arrives in one it can.
    /// </summary>
    internal static string AsJpeg(string url)
    {
        var lastSlash = url.LastIndexOf('/');
        var dot = url.LastIndexOf('.');
        return dot > lastSlash ? url[..dot] + ".jpg" : url + ".jpg";
    }

    private async Task UploadAsync(byte[] png, string storageKey, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(png);
        await images.UploadImageAsync(new UploadFileDto($"{Guid.NewGuid():N}.png", "image/png", stream.Length, stream), storageKey, cancellationToken);
    }
}
