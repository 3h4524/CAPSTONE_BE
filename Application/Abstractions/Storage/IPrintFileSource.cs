namespace APCS.Application.Abstractions.Storage;

/// <summary>
/// The print file of a stored design: the same image delivered at the width the print needs, so Printify receives
/// enough pixels for the size it prints. Only builds a URL; the storage provider resizes on first delivery.
/// </summary>
public interface IPrintFileSource
{
    /// <summary>
    /// A URL serving <paramref name="imageUrl"/> <paramref name="widthPx"/> pixels wide (upscaled when the design is
    /// smaller), or null when the image is not stored where it can be resized on delivery.
    /// </summary>
    string? Resized(string imageUrl, int sourceWidthPx, int sourceHeightPx, int widthPx);
}
