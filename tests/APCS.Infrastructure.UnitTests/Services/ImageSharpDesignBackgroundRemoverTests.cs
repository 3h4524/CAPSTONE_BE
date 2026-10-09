using APCS.Infrastructure.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class ImageSharpDesignBackgroundRemoverTests
{
    private readonly ImageSharpDesignBackgroundRemover _remover = new();

    [TestMethod]
    public void RemoveBackground_ArtworkOnWhite_MakesOnlyTheBackdropTransparent()
    {
        // A navy ring (with a white inside) and a stray 2px speck, on white.
        var image = Png(300, 300, (x, y) =>
        {
            var d = Math.Sqrt((x - 150) * (x - 150) + (y - 150) * (y - 150));
            if (d is >= 60 and < 90) return new Rgba32(31, 42, 68);
            if (x is >= 20 and < 22 && y is >= 20 and < 22) return new Rgba32(120, 120, 120);
            return new Rgba32(255, 255, 255);
        });

        var png = _remover.RemoveBackground(image);

        png.Should().NotBeNull();
        using var result = Image.Load<Rgba32>(png!);
        result[5, 5].A.Should().Be(0, "the backdrop is removed");
        result[150, 75].A.Should().Be(255, "the artwork is kept");
        result[150, 150].A.Should().Be(255, "white enclosed by the artwork is part of it");
        result[21, 21].A.Should().Be(0, "specks of the backdrop are dropped");
    }

    [TestMethod]
    public void RemoveBackground_FullBleedArtwork_KeepsTheImage()
    {
        var image = Png(200, 200, (x, y) => new Rgba32((byte)x, (byte)y, 128));

        _remover.RemoveBackground(image).Should().BeNull();
    }

    [TestMethod]
    public void RemoveBackground_ImageInAFormatThatIsNotRead_IsRefused()
    {
        var remove = () => _remover.RemoveBackground(ImageSharpMockupMapGeneratorTests.Encoded("tiff", 120, 120));

        remove.Should().Throw<UnknownImageFormatException>("the caller keeps such an image as it was generated");
    }

    private static byte[] Png(int width, int height, Func<int, int, Rgba32> color)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                image[x, y] = color(x, y);

        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }
}
