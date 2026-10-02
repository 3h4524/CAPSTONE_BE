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
    string BuildCompositeUrl(string baseImageUrl, string overlayStorageKey, MockupPosition position);
}

/// <summary>A print-area rectangle in pixels, relative to the top-left corner of the base image.</summary>
public sealed record MockupPosition(int X, int Y, int Width, int Height);
