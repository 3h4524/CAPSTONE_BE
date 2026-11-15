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

    /// <summary>
    /// Returns a URL that renders several designs on <paramref name="baseImageUrl"/>, one per placement, such as a
    /// chest print, a sleeve logo and a back print on the same photo. The design sizes on
    /// <paramref name="layers"/> and its <see cref="MockupLayers.DesignCorners"/> / <see cref="MockupLayers.MorePlacements"/>
    /// are not used: every placement carries its own.
    /// </summary>
    string BuildCompositeUrl(string baseImageUrl, IReadOnlyList<MockupPlacement> placements, MockupLayers? layers = null);

    /// <summary>The PNG delivery URL of another image stored with the same provider as <paramref name="baseImageUrl"/>.</summary>
    string BuildAssetUrl(string baseImageUrl, string storageKey);
}

/// <summary>A print-area rectangle in pixels, relative to the top-left corner of the base image.</summary>
public sealed record MockupPosition(int X, int Y, int Width, int Height);

/// <summary>A point in pixels, relative to the top-left corner of the base image.</summary>
public sealed record MockupPoint(double X, double Y);

/// <summary>One design laid on a mock-up photo.</summary>
/// <param name="OverlayStorageKey">The design image's storage key, as on <c>DesignImage.StorageKey</c>.</param>
/// <param name="Area">The print area it is fitted into and centered in.</param>
/// <param name="DesignWidthPx">The design image's own width; with the height it lets the design be fitted exactly.</param>
/// <param name="DesignHeightPx">The design image's own height.</param>
/// <param name="Rotation">Degrees the design is turned about the print area's center, clockwise; 0 keeps it straight.</param>
/// <param name="Corners">Where the design's four corners go instead (top-left, top-right, bottom-right, bottom-left).</param>
public sealed record MockupPlacement(
    string OverlayStorageKey,
    MockupPosition Area,
    int? DesignWidthPx,
    int? DesignHeightPx,
    double Rotation = 0,
    IReadOnlyList<MockupPoint>? Corners = null);

/// <summary>Turning rectangles on a photo.</summary>
public static class MockupGeometry
{
    /// <summary>
    /// The corners of <paramref name="rect"/> turned <paramref name="degrees"/> clockwise about its center: top-left,
    /// top-right, bottom-right, bottom-left of the rectangle as it was.
    /// </summary>
    public static IReadOnlyList<MockupPoint> RotateCorners(MockupPosition rect, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        var centerX = rect.X + rect.Width / 2.0;
        var centerY = rect.Y + rect.Height / 2.0;
        MockupPoint Turn(double x, double y)
        {
            var (dx, dy) = (x - centerX, y - centerY);
            // Image rows grow downwards, so this turns clockwise as seen. Rounded so a quarter turn's floating-point
            // dust does not widen the box around the corners by a pixel.
            return new MockupPoint(Math.Round(centerX + dx * cos - dy * sin, 6), Math.Round(centerY + dx * sin + dy * cos, 6));
        }
        return
        [
            Turn(rect.X, rect.Y),
            Turn(rect.X + rect.Width, rect.Y),
            Turn(rect.X + rect.Width, rect.Y + rect.Height),
            Turn(rect.X, rect.Y + rect.Height),
        ];
    }

    /// <summary>The pixel box around <paramref name="rect"/> turned <paramref name="degrees"/> about its center.</summary>
    public static MockupPosition RotatedBounds(MockupPosition rect, double degrees)
    {
        if (degrees == 0)
            return rect;
        var corners = RotateCorners(rect, degrees);
        var left = (int)Math.Floor(corners.Min(point => point.X));
        var top = (int)Math.Floor(corners.Min(point => point.Y));
        var right = (int)Math.Ceiling(corners.Max(point => point.X));
        var bottom = (int)Math.Ceiling(corners.Max(point => point.Y));
        return new MockupPosition(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }
}

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
/// <param name="CutMaskKey">
/// Storage key of the template's cut mask (the garment mask with its edge pulled in). With
/// <paramref name="BackgroundColor"/> the finished garment is cut out with it and put on that color.
/// </param>
/// <param name="BackgroundColor">'#RRGGBB' to replace the photo's background with.</param>
/// <param name="BasePhotoWidthPx">The base photo's width; a replaced background is cropped back to the photo's size.</param>
/// <param name="BasePhotoHeightPx">The base photo's height.</param>
/// <param name="DesignCorners">
/// Where the design's four corners go (top-left, top-right, bottom-right, bottom-left), for a print area that is
/// not square to the photo, such as a chest turned slightly away. The design is warped onto them instead of
/// being fitted into the print-area rectangle.
/// </param>
/// <param name="MorePlacements">
/// More print areas the same design goes in, each fitted as the first one is: one per person on a photo of several
/// people all wearing the mock-up. The garment is recolored once for all of them.
/// </param>
public sealed record MockupLayers(
    int? DesignWidthPx,
    int? DesignHeightPx,
    string? DisplacementMapKey,
    string? GarmentMaskKey,
    string? GarmentColor,
    bool MultiplyDesign = false,
    int? BasePhotoShortSidePx = null,
    string? CutMaskKey = null,
    string? BackgroundColor = null,
    int? BasePhotoWidthPx = null,
    int? BasePhotoHeightPx = null,
    IReadOnlyList<MockupPoint>? DesignCorners = null,
    IReadOnlyList<MockupPosition>? MorePlacements = null);
