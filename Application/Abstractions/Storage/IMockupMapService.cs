namespace APCS.Application.Abstractions.Storage;

/// <summary>Turns a mock-up base photo into what compositing needs, and stores the helper images.</summary>
public interface IMockupMapService
{
    /// <summary>Whether photos can be processed at all (the model that finds the garment is installed).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Finds the garment in <paramref name="photo"/> and derives its helper images.
    /// </summary>
    /// <param name="photo">The photo's bytes, in any common image format.</param>
    /// <param name="preview">A small, quick rendition for showing in a form; nothing meant for storing.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<PreparedBasePhoto> PrepareAsync(byte[] photo, bool preview = false, CancellationToken cancellationToken = default);

    /// <summary>Downloads a stored base photo in a format <see cref="PrepareAsync"/> can read.</summary>
    Task<byte[]> DownloadAsync(string baseImageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the helper images of <paramref name="prepared"/> under <paramref name="keyPrefix"/>
    /// plus "-displace", "-mask" and, when the garment was separated, "-cut".
    /// </summary>
    Task StoreHelpersAsync(string keyPrefix, PreparedBasePhoto prepared, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure the cut mask of a template exists, deriving it from the stored garment mask for a
    /// template analyzed before cut masks were made.
    /// </summary>
    /// <param name="maskUrl">Delivery URL of the stored garment mask.</param>
    /// <param name="cutMaskUrl">Delivery URL the cut mask has once stored.</param>
    /// <param name="cutMaskKey">Storage key to store the cut mask under.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="false"/> when the garment was not separated from the background, so there is nothing to cut.</returns>
    Task<bool> EnsureCutMaskAsync(string maskUrl, string cutMaskUrl, string cutMaskKey, CancellationToken cancellationToken = default);
}

/// <summary>How much of the photo the garment covers and how bright it is, both 0..1.</summary>
public sealed record GarmentMaskStats(double Coverage, double Luminance);

/// <summary>A base photo's garment mask, measurements and derived images.</summary>
/// <param name="MaskPng">The garment mask: alpha is the garment, gray is the fabric's shading.</param>
/// <param name="DisplacementPng">The fabric relief map; <see langword="null"/> for a preview.</param>
/// <param name="Stats">The garment mask measurements.</param>
/// <param name="WidthPx">The photo's width in pixels (of the preview rendition, for a preview).</param>
/// <param name="HeightPx">The photo's height in pixels.</param>
/// <param name="CutMaskPng">The mask for lifting the garment onto another background; <see langword="null"/> for a preview or when no garment was separated.</param>
public sealed record PreparedBasePhoto(
    byte[] MaskPng, byte[]? DisplacementPng, GarmentMaskStats Stats, int WidthPx = 0, int HeightPx = 0, byte[]? CutMaskPng = null);
