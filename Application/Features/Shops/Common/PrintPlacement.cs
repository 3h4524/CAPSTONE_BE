using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.Shops.Common;

/// <summary>
/// Where the design goes in a Printify print area, in Printify's terms: <see cref="X"/> and <see cref="Y"/> are its
/// center as a share of the area's width and height, <see cref="Scale"/> its width as a share of the area's width and
/// <see cref="Angle"/> how far it is turned about its center, in whole degrees clockwise.
/// </summary>
/// <param name="Adjusted">The mock-up put the design partly outside the print area, so it was made smaller or moved in.</param>
public sealed record PrintPlacement(double X, double Y, double Scale, bool Adjusted = false, int Angle = 0)
{
    /// <summary>The design as large as the print area allows, centered: what a mock-up shows when nobody moved it.</summary>
    public static PrintPlacement Fitted(int designWidth, int designHeight, int areaWidth, int areaHeight) =>
        FromMockup(null, null, designWidth, designHeight, areaWidth, areaHeight);

    /// <summary>
    /// The placement a mock-up shows. The frame is Printify's print area on the template photo, which an Admin
    /// measured when linking the template; the box is where the mock-up puts the design inside it (the template's own
    /// place, or one the seller dragged, resized or turned). A mock-up draws the design as large as fits its box,
    /// centered and turned about the box's center, and so does Printify here.
    /// </summary>
    /// <param name="template">Printify's print area on the template photo, in its pixels.</param>
    /// <param name="custom">The box the design is drawn in, or null for the whole print area.</param>
    /// <param name="rotation">Degrees the box is turned clockwise, as the mock-up shows it.</param>
    public static PrintPlacement FromMockup(MockupPosition? template, MockupPosition? custom, int designWidth, int designHeight,
        int areaWidth, int areaHeight, double rotation = 0)
    {
        if (designWidth <= 0 || designHeight <= 0 || areaWidth <= 0 || areaHeight <= 0)
            return new PrintPlacement(0.5, 0.5, 1);

        // As large as fits the print area: full width, unless the design is taller than the area.
        var fitted = Math.Min(1, (double)areaHeight * designWidth / ((double)areaWidth * designHeight));
        double x = 0.5, y = 0.5, size = 1;
        var angle = 0;
        if (template is { Width: > 0, Height: > 0 } area && custom is { Width: > 0, Height: > 0 } box)
        {
            angle = WholeDegrees(rotation);
            if (box != area)
            {
                var full = Contained(area, designWidth, designHeight);
                var shown = Contained(box, designWidth, designHeight);
                size = shown.Width / full.Width;
                x = (shown.CenterX - area.X) / area.Width;
                y = (shown.CenterY - area.Y) / area.Height;
            }
        }

        // Larger than the print area, or reaching past its edge, would be cut off on the garment. A turned design
        // reaches as far as the box around it.
        var scale = fitted * Math.Min(size, 1);
        var (halfWidth, halfHeight) = HalfExtents(scale, angle, designWidth, designHeight, areaWidth, areaHeight);
        var shrink = Math.Min(1, Math.Min(0.5 / halfWidth, 0.5 / halfHeight));
        if (shrink < 1)
        {
            scale *= shrink;
            (halfWidth, halfHeight) = (halfWidth * shrink, halfHeight * shrink);
        }
        var inX = Math.Clamp(x, halfWidth, 1 - halfWidth);
        var inY = Math.Clamp(y, halfHeight, 1 - halfHeight);
        var adjusted = size > 1.001 || shrink < 0.999 || Math.Abs(inX - x) > 0.001 || Math.Abs(inY - y) > 0.001;
        return new PrintPlacement(Math.Round(inX, 4), Math.Round(inY, 4), Math.Round(scale, 4), adjusted, angle);
    }

    /// <summary>How many pixels wide the design is printed, so the print file can be made that large.</summary>
    public int PrintedWidthPx(int areaWidth) => (int)Math.Ceiling(Scale * areaWidth);

    // Printify takes whole degrees; -180 to 180, as the mock-up editor keeps them.
    private static int WholeDegrees(double rotation)
    {
        if (!double.IsFinite(rotation))
            return 0;
        var degrees = (int)Math.Round(rotation % 360);
        return degrees > 180 ? degrees - 360 : degrees <= -180 ? degrees + 360 : degrees;
    }

    // Half the width and height of the box around the turned design, as shares of the print area's.
    private static (double Width, double Height) HalfExtents(double scale, int angle, int designWidth, int designHeight, int areaWidth, int areaHeight)
    {
        var width = scale * areaWidth;
        var height = width * designHeight / designWidth;
        var radians = angle * Math.PI / 180;
        var (cos, sin) = (Math.Abs(Math.Cos(radians)), Math.Abs(Math.Sin(radians)));
        return ((width * cos + height * sin) / 2 / areaWidth, (width * sin + height * cos) / 2 / areaHeight);
    }

    private static (double Width, double CenterX, double CenterY) Contained(MockupPosition box, int designWidth, int designHeight)
    {
        var ratio = Math.Min((double)box.Width / designWidth, (double)box.Height / designHeight);
        return (designWidth * ratio, box.X + box.Width / 2.0, box.Y + box.Height / 2.0);
    }
}
