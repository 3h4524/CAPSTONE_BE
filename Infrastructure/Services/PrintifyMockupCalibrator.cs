using System.Globalization;
using APCS.Application.Abstractions.Commerce;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Finds print areas and garment colors on Printify's mock-up photos by plain pixel rules: the print area is where
/// the magenta calibration sheet shows, and a color is the fabric of that garment over the white one's.
/// </summary>
public sealed class PrintifyMockupCalibrator(IHttpClientFactory clients) : IPrintifyMockupCalibrator
{
    public const string HttpClientName = "PrintifyMockups";

    /// <summary>The shorter side a photo needs: a template photo is shown and recolored at full size.</summary>
    internal const int MinimumPhotoSide = 1500;

    internal const int MaximumDownloadBytes = 20 * 1024 * 1024;

    // The API hands out images-api.printify.com links; images.printify.com serves the same photos.
    private static readonly string[] MockupHosts = ["images-api.printify.com", "images.printify.com"];

    public async Task<byte[]> DownloadMockupAsync(string url, CancellationToken cancellationToken)
    {
        var address = MockupUrl(url) ?? throw new InvalidOperationException("Only Printify mock-up photos are downloaded.");
        using var response = await clients.CreateClient(HttpClientName).GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Printify returned {(int)response.StatusCode} for a mock-up photo.");
        if (response.Content.Headers.ContentLength > MaximumDownloadBytes)
            throw new InvalidOperationException("The mock-up photo is too large.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumDownloadBytes)
                throw new InvalidOperationException("The mock-up photo is too large.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// A Printify mock-up URL asking for the 2048 px photo, or null for any other address (another host, plain http,
    /// not a mock-up).
    /// </summary>
    internal static Uri? MockupUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !MockupHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) || !uri.IsDefaultPort
            || !uri.AbsolutePath.StartsWith("/mockup/", StringComparison.Ordinal) || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("s=", StringComparison.Ordinal))
            .Append("s=2048");
        return new UriBuilder(uri) { Query = string.Join('&', query) }.Uri;
    }

    public PrintAreaDetection DetectPrintArea(byte[] calibration, byte[] blank)
    {
        using var sheet = Image.Load<Rgb24>(calibration);
        using var bare = Image.Load<Rgb24>(blank);
        int width = sheet.Width, height = sheet.Height;
        if (bare.Width != width || bare.Height != height)
            return new(null, width, height, "The two photos of this camera are not the same size.");
        if (Math.Min(width, height) < MinimumPhotoSide)
            return new(null, width, height, $"The photo is {width}x{height} px; a template needs at least {MinimumPhotoSide} px on its shorter side.");

        var columns = new int[width];
        var rows = new int[height];
        var total = 0;
        sheet.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (!IsMagenta(row[x]))
                        continue;
                    columns[x]++;
                    rows[y]++;
                    total++;
                }
            }
        });
        // Below this the print area does not face this camera (a sleeve seen from the front, a folded shot…).
        if (total < width * height / 500)
            return new(null, width, height, "The print area does not show on this photo.");

        // Stray pink pixels elsewhere do not stretch the area: only rows and columns with a real share of magenta count.
        var (left, right) = Span(columns, columns.Max() / 20);
        var (top, bottom) = Span(rows, rows.Max() / 20);
        var area = new PrintAreaOnPhoto(left, top, right - left + 1, bottom - top + 1);
        var inside = 0;
        sheet.ProcessPixelRows(accessor =>
        {
            for (var y = area.Y; y < area.Y + area.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = area.X; x < area.X + area.Width; x++)
                    if (IsMagenta(row[x])) inside++;
            }
        });
        // A flat print area is nearly all magenta (less the cross and the marker); a folded or turned one is not.
        if (inside < (long)area.Width * area.Height / 2)
            return new(null, width, height, "The print area is not a flat rectangle on this photo.");

        var leftOver = 0;
        bare.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    if (IsMagenta(row[x])) leftOver++;
            }
        });
        if (leftOver > total / 200)
            return new(null, width, height, "The bare photo still shows the calibration sheet.");

        if (!SameFraming(sheet, bare, area))
            return new(null, width, height, "The two photos of this camera are framed differently.");

        return new(area, width, height, null);
    }

    public FabricMeasurement? MeasureFabric(byte[] colorPhoto, byte[] whitePhoto, PrintAreaOnPhoto area)
    {
        using var color = Image.Load<Rgb24>(colorPhoto);
        using var white = Image.Load<Rgb24>(whitePhoto);
        if (color.Width != white.Width || color.Height != white.Height)
            return null;
        var middle = Middle(area, color.Width, color.Height);
        if (middle.Width <= 0 || middle.Height <= 0)
            return null;
        var fabric = Median(color, middle);
        var bare = Median(white, middle);
        var recolor = new int[3];
        for (var channel = 0; channel < 3; channel++)
            recolor[channel] = bare[channel] == 0 ? 0 : (int)Math.Round(Math.Min(255.0, fabric[channel] * 255.0 / bare[channel]));
        return new FabricMeasurement(Hex(fabric), Hex(recolor));
    }

    public (byte[] Calibration, byte[] Blank) Sheets() => (Resource("APCS.Printify.Calibration.png"), Resource("APCS.Printify.Blank.png"));

    private static byte[] Resource(string name)
    {
        using var stream = typeof(PrintifyMockupCalibrator).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The resource {name} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static bool IsMagenta(Rgb24 pixel) => pixel.R > 180 && pixel.B > 180 && pixel.G < 90;

    // The first and last index whose count reaches the threshold.
    private static (int From, int To) Span(int[] counts, int threshold)
    {
        var minimum = Math.Max(1, threshold);
        var from = Array.FindIndex(counts, count => count >= minimum);
        var to = Array.FindLastIndex(counts, count => count >= minimum);
        return (from, to);
    }

    // Outside the print area (and a margin for its edge) the two photos of one camera are the same picture.
    private static bool SameFraming(Image<Rgb24> sheet, Image<Rgb24> bare, PrintAreaOnPhoto area)
    {
        var margin = Math.Max(8, Math.Min(sheet.Width, sheet.Height) / 100);
        long difference = 0, compared = 0;
        for (var y = 0; y < sheet.Height; y += 4)
        {
            var nearRows = y >= area.Y - margin && y < area.Y + area.Height + margin;
            for (var x = 0; x < sheet.Width; x += 4)
            {
                if (nearRows && x >= area.X - margin && x < area.X + area.Width + margin)
                    continue;
                var a = sheet[x, y];
                var b = bare[x, y];
                difference += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                compared += 3;
            }
        }
        return compared > 0 && (double)difference / compared <= 8;
    }

    // The middle of the print area, clear of its edges and seams: where the fabric shows evenly.
    internal static PrintAreaOnPhoto Middle(PrintAreaOnPhoto area, int width, int height)
    {
        var left = Math.Clamp(area.X + (int)Math.Round(area.Width * 0.3), 0, width);
        var top = Math.Clamp(area.Y + (int)Math.Round(area.Height * 0.25), 0, height);
        var right = Math.Clamp(left + (int)Math.Round(area.Width * 0.4), 0, width);
        var bottom = Math.Clamp(top + (int)Math.Round(area.Height * 0.4), 0, height);
        return new PrintAreaOnPhoto(left, top, right - left, bottom - top);
    }

    private static int[] Median(Image<Rgb24> image, PrintAreaOnPhoto box)
    {
        var channels = new[] { new List<byte>(), new List<byte>(), new List<byte>() };
        image.ProcessPixelRows(accessor =>
        {
            for (var y = box.Y; y < box.Y + box.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = box.X; x < box.X + box.Width; x++)
                {
                    channels[0].Add(row[x].R);
                    channels[1].Add(row[x].G);
                    channels[2].Add(row[x].B);
                }
            }
        });
        return channels.Select(values =>
        {
            values.Sort();
            return (int)values[values.Count / 2];
        }).ToArray();
    }

    private static string Hex(IReadOnlyList<int> rgb) =>
        "#" + string.Concat(rgb.Select(value => Math.Clamp(value, 0, 255).ToString("X2", CultureInfo.InvariantCulture)));
}

/// <summary>Reads the server's Printify token from <c>Printify:CatalogToken</c>.</summary>
public sealed class PrintifyAdminToken(Microsoft.Extensions.Options.IOptions<Options.PrintifyOptions> options) : IPrintifyAdminToken
{
    public string? Token => string.IsNullOrWhiteSpace(options.Value.CatalogToken) ? null : options.Value.CatalogToken.Trim();
}
