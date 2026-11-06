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

    /// <summary>
    /// Derives the helper images of a photo of a person wearing the garment. The garment is the part
    /// of the person in <paramref name="garmentSelection"/>, so nothing else of them is recolored.
    /// </summary>
    /// <param name="photo">The photo's bytes, in any common image format.</param>
    /// <param name="garmentSelection">
    /// A PNG whose alpha is the garment, at the photo's size or scaled down from it; null finds the garment the
    /// person wears automatically.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The helper images; null when the garment was to be found automatically and none was.</returns>
    Task<PreparedBasePhoto?> PrepareModelPhotoAsync(byte[] photo, byte[]? garmentSelection, CancellationToken cancellationToken = default);

    /// <summary>
    /// The person the model finds in a photo, for selecting the garment on it: the mask's alpha is the
    /// person, at most <c>ModelPreviewMaxSide</c> on its longest side.
    /// </summary>
    Task<PreparedBasePhoto> PreviewModelPhotoAsync(byte[] photo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures the garment in a stored garment mask: where it starts and how wide its body is below the
    /// sleeves. <see langword="null"/> when the mask separated no garment.
    /// </summary>
    /// <param name="garmentMaskUrl">Delivery URL of the stored garment mask.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<GarmentMeasurement?> MeasureGarmentAsync(string garmentMaskUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures each garment in a stored garment mask of several people, from left to right: the separate parts of
    /// the mask. Empty when it separated none.
    /// </summary>
    /// <param name="garmentMaskUrl">Delivery URL of the stored garment mask.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<IReadOnlyList<GarmentMeasurement>> MeasureGarmentsAsync(string garmentMaskUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// The color of the garment in a stored photo as '#RRGGBB', read from its well-lit part.
    /// <see langword="null"/> when the mask separated no garment.
    /// </summary>
    /// <param name="photoUrl">Delivery URL of the photo.</param>
    /// <param name="garmentMaskUrl">Delivery URL of the photo's garment mask.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<string?> MeasureGarmentColorAsync(string photoUrl, string garmentMaskUrl, CancellationToken cancellationToken = default);

    /// <summary>Downloads a stored base photo in a format <see cref="PrepareAsync"/> can read.</summary>
    Task<byte[]> DownloadAsync(string baseImageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// The photo as a high-quality JPEG: a large PNG photo (as an image generator returns) would be over the upload
    /// size limit for no visible gain.
    /// </summary>
    Task<byte[]> ToJpegAsync(byte[] photo, CancellationToken cancellationToken = default);

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

/// <summary>
/// Where a garment sits in its photo, as shares of the photo's width and height (0..1): the top of the garment
/// (its collar and shoulders), its bottom, and the center and width of its body below the sleeves.
/// </summary>
public sealed record GarmentMeasurement(double Top, double Bottom, double CenterX, double BodyWidth);

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
    byte[] MaskPng, byte[]? DisplacementPng, GarmentMaskStats Stats, int WidthPx = 0, int HeightPx = 0, byte[]? CutMaskPng = null,
    byte[]? WornGarmentPng = null, int? GarmentCount = null);
