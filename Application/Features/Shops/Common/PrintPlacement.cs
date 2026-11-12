using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.Shops.Common;

/// <summary>
/// Where the design goes in a Printify print area, in Printify's terms: <see cref="X"/> and <see cref="Y"/> are its
/// center as a share of the area's width and height, <see cref="Scale"/> its width as a share of the area's width.
/// </summary>
/// <param name="Adjusted">The mock-up put the design partly outside the print area, so it was made smaller or moved in.</param>
public sealed record PrintPlacement(double X, double Y, double Scale, bool Adjusted = false)
{
    /// <summary>The design as large as the print area allows, centered: what a mock-up shows when nobody moved it.</summary>
    public static PrintPlacement Fitted(int designWidth, int designHeight, int areaWidth, int areaHeight) =>
        FromMockup(null, null, designWidth, designHeight, areaWidth, areaHeight);

    /// <summary>
    /// The placement a mock-up shows. The template's own print area stands for Printify's print area, which an Admin
    /// checked when linking the template; a box the seller dragged or resized on the mock-up moves and sizes the design
    /// inside it. A mock-up draws the design as large as fits its box, centered, and so does Printify here.
    /// </summary>
    /// <param name="template">The template's print area, in the template photo's pixels.</param>
    /// <param name="custom">The box the seller set on this mock-up, or null.</param>
    public static PrintPlacement FromMockup(MockupPosition? template, MockupPosition? custom, int designWidth, int designHeight, int areaWidth, int areaHeight)
    {
        if (designWidth <= 0 || designHeight <= 0 || areaWidth <= 0 || areaHeight <= 0)
            return new PrintPlacement(0.5, 0.5, 1);

        // As large as fits the print area: full width, unless the design is taller than the area.
        var fitted = Math.Min(1, (double)areaHeight * designWidth / ((double)areaWidth * designHeight));
        double x = 0.5, y = 0.5, size = 1;
        if (template is { Width: > 0, Height: > 0 } area && custom is { Width: > 0, Height: > 0 } box && box != area)
        {
            var full = Contained(area, designWidth, designHeight);
            var shown = Contained(box, designWidth, designHeight);
            size = shown.Width / full.Width;
            x = (shown.CenterX - area.X) / area.Width;
            y = (shown.CenterY - area.Y) / area.Height;
        }

        // Larger than the print area, or reaching past its edge, would be cut off on the garment.
        var scale = fitted * Math.Min(size, 1);
        var halfWidth = scale / 2;
        var halfHeight = scale * areaWidth * designHeight / ((double)designWidth * areaHeight) / 2;
        var inX = Math.Clamp(x, halfWidth, 1 - halfWidth);
        var inY = Math.Clamp(y, halfHeight, 1 - halfHeight);
        var adjusted = size > 1.001 || Math.Abs(inX - x) > 0.001 || Math.Abs(inY - y) > 0.001;
        return new PrintPlacement(Math.Round(inX, 4), Math.Round(inY, 4), Math.Round(scale, 4), adjusted);
    }

    /// <summary>How many pixels wide the design is printed, so the print file can be made that large.</summary>
    public int PrintedWidthPx(int areaWidth) => (int)Math.Ceiling(Scale * areaWidth);

    private static (double Width, double CenterX, double CenterY) Contained(MockupPosition box, int designWidth, int designHeight)
    {
        var ratio = Math.Min((double)box.Width / designWidth, (double)box.Height / designHeight);
        return (designWidth * ratio, box.X + box.Width / 2.0, box.Y + box.Height / 2.0);
    }
}
