using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class SegFormerGarmentLocatorTests
{
    [TestMethod]
    public void Box_GarmentBlock_ReturnsItsBoxAsShares()
    {
        var mask = Mask(100, 200, (20, 60, 80, 140));

        var box = SegFormerGarmentLocator.Box(mask, 100, 200);

        box.Should().NotBeNull();
        box!.X.Should().BeApproximately(0.2, 0.011);
        box.Y.Should().BeApproximately(0.3, 0.011);
        box.Width.Should().BeApproximately(0.6, 0.021);
        box.Height.Should().BeApproximately(0.4, 0.021);
    }

    [TestMethod]
    public void Box_StrayPatchFarFromGarment_DoesNotStretchBox()
    {
        var mask = Mask(100, 200, (20, 60, 80, 140));
        mask[2 * 100 + 98] = 255;

        var box = SegFormerGarmentLocator.Box(mask, 100, 200);

        box.Should().NotBeNull();
        (box!.X + box.Width).Should().BeLessThan(0.82);
        box.Y.Should().BeGreaterThan(0.29);
    }

    [TestMethod]
    public void Box_TooLittleGarment_ReturnsNull()
    {
        var mask = Mask(100, 200, (10, 10, 12, 12));

        SegFormerGarmentLocator.Box(mask, 100, 200).Should().BeNull();
    }

    [TestMethod]
    public void Box_NoMask_ReturnsNull()
    {
        SegFormerGarmentLocator.Box(null, 100, 200).Should().BeNull();
    }

    private static byte[] Mask(int width, int height, (int Left, int Top, int Right, int Bottom) garment)
    {
        var mask = new byte[width * height];
        for (var y = garment.Top; y < garment.Bottom; y++)
            for (var x = garment.Left; x < garment.Right; x++)
                mask[y * width + x] = 230;
        return mask;
    }
}
