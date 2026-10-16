namespace APCS.Application.Abstractions.Storage;

/// <summary>
/// Builds a delivery URL that composites a design image onto a mock-up base photo. Pure and
/// synchronous: both images are already stored, so this only builds a URL string — the actual
/// pixel compositing happens on the storage provider's CDN the first time someone requests it.
/// </summary>
public interface IMockupCompositor
{
    /// <summary>
    /// Returns a URL that renders the design image at <paramref name="overlayStorageKey"/> placed
    /// inside <paramref name="position"/> on top of <paramref name="baseImageUrl"/>.
    /// </summary>
    /// <param name="baseImageUrl">The base mock-up photo's existing public delivery URL.</param>
    /// <param name="overlayStorageKey">
    /// The design image's storage key exactly as stored on <c>DesignImage.StorageKey</c> (not a URL,
    /// and not yet a full Cloudinary public id — the implementation resolves that from the storage
    /// key the same way the upload path did).
    /// </param>
    /// <param name="position">Where to place the overlay, in the base image's own pixel space.</param>
    /// <param name="layers">Optional realism layers; <see langword="null"/> gives a plain overlay.</param>
    string BuildCompositeUrl(string baseImageUrl, string overlayStorageKey, MockupPosition position, MockupLayers? layers = null);

    /// <summary>The PNG delivery URL of another image stored with the same provider as <paramref name="baseImageUrl"/>.</summary>
    string BuildAssetUrl(string baseImageUrl, string storageKey);
}

/// <summary>A print-area rectangle in pixels, relative to the top-left corner of the base image.</summary>
public sealed record MockupPosition(int X, int Y, int Width, int Height);

/// <summary>
/// Extra layers for a realistic composite. Every part is optional and is skipped when missing.
/// </summary>
/// <param name="DesignWidthPx">The design image's own width; the displacement map needs it (and the height) to line up.</param>
/// <param name="DesignHeightPx">The design image's own height.</param>
/// <param name="DisplacementMapKey">Storage key of the template's displacement map.</param>
/// <param name="GarmentMaskKey">
/// Storage key of the template's garment mask (alpha = garment, gray = fabric shading). The design
/// is cut to it, so nothing is printed off the garment; with <paramref name="GarmentColor"/> it
/// also recolors the garment.
/// </param>
/// <param name="GarmentColor">'#RRGGBB' to recolor the garment with.</param>
/// <param name="MultiplyDesign">
/// Blend the design with multiply, so a light garment's folds and texture show through it.
/// </param>
/// <param name="BasePhotoShortSidePx">
/// The base photo's shorter side. Folds are as wide as the photo makes them, whatever the design's
/// size, so this limits how far they shift a large design.
/// </param>
public sealed record MockupLayers(
    int? DesignWidthPx,
    int? DesignHeightPx,
    string? DisplacementMapKey,
    string? GarmentMaskKey,
    string? GarmentColor,
    bool MultiplyDesign = false,
    int? BasePhotoShortSidePx = null);
