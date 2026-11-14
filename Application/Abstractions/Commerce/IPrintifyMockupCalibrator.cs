namespace APCS.Application.Abstractions.Commerce;

/// <summary>Where a print area is on a photo, in its pixels.</summary>
public sealed record PrintAreaOnPhoto(int X, int Y, int Width, int Height);

/// <summary>
/// The print area found on a mock-up photo, or why none was. <c>Width</c> and <c>Height</c> are the photo's size.
/// </summary>
public sealed record PrintAreaDetection(PrintAreaOnPhoto? Area, int Width, int Height, string? Problem)
{
    public bool Found => Area is not null && Problem is null;
}

/// <summary>
/// A garment color measured on Printify's photo: the fabric as photographed, and the color that, laid over the
/// white photo the way mock-ups are recolored, gives that photo.
/// </summary>
public sealed record FabricMeasurement(string PhotoHex, string RecolorHex);

/// <summary>
/// Reads Printify's mock-up photos without AI: the print area is the magenta of a calibration sheet printed there,
/// and a garment color is its fabric compared with the white garment's.
/// </summary>
public interface IPrintifyMockupCalibrator
{
    /// <summary>
    /// Downloads a Printify mock-up photo at 2048 px. Only <c>https://images-api.printify.com/mockup/…</c> (or images.printify.com) is fetched, with
    /// no redirects and a size limit.
    /// </summary>
    Task<byte[]> DownloadMockupAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// The print area on <paramref name="blank"/>: the magenta on <paramref name="calibration"/>, the same camera with
    /// the calibration sheet printed. Both must be the same large photo, framed the same, and the blank must show no magenta.
    /// </summary>
    PrintAreaDetection DetectPrintArea(byte[] calibration, byte[] blank);

    /// <summary>The fabric color in the middle of <paramref name="area"/> on a colored garment, against the white one.</summary>
    FabricMeasurement? MeasureFabric(byte[] colorPhoto, byte[] whitePhoto, PrintAreaOnPhoto area);

    /// <summary>The calibration sheet (magenta, 1:3 cross) and the near-transparent sheet that shows the garment bare.</summary>
    (byte[] Calibration, byte[] Blank) Sheets();
}
