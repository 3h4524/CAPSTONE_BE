using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Prepares mock-up base photos with <see cref="IMockupMapGenerator"/> and stores the derived
/// helper images next to the photo.
/// </summary>
public sealed class MockupMapService(
    IHttpClientFactory clients,
    IMockupMapGenerator generator,
    IPublicImageService images) : IMockupMapService
{
    internal const string HttpClientName = "MockupMaps";
    internal const int PreviewMaxSide = 800;

    public bool IsAvailable => generator.IsAvailable;

    public Task<PreparedBasePhoto> PrepareAsync(byte[] photo, bool preview = false, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var garment = generator.GenerateGarmentMask(photo, preview ? PreviewMaxSide : null);
            var displacement = preview ? null : generator.GenerateDisplacementMap(photo);
            return new PreparedBasePhoto(
                garment.MaskPng, displacement,
                new GarmentMaskStats(garment.GarmentCoverage, garment.GarmentLuminance), garment.WidthPx, garment.HeightPx);
        }, cancellationToken);

    public Task<byte[]> DownloadAsync(string baseImageUrl, CancellationToken cancellationToken = default) =>
        clients.CreateClient(HttpClientName).GetByteArrayAsync(AsJpeg(baseImageUrl), cancellationToken);

    public async Task StoreHelpersAsync(string keyPrefix, PreparedBasePhoto prepared, CancellationToken cancellationToken = default)
    {
        if (prepared.DisplacementPng is { } displacement)
            await UploadAsync(displacement, MockupRules.DisplacementMapKey(keyPrefix), cancellationToken);
        await UploadAsync(prepared.MaskPng, MockupRules.GarmentMaskKey(keyPrefix), cancellationToken);
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
