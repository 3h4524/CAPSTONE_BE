using APCS.Infrastructure.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class ImageSharpMockupMapGeneratorTests
{
    // A model that takes the middle of the photo (20%..80% of each side) for the garment.
    private static readonly FakeGarmentSegmenter Middle = new((x, y) => x is >= 0.2 and < 0.8 && y is >= 0.2 and < 0.8);

    private readonly ImageSharpMockupMapGenerator _generator = new(Middle);

    [TestMethod]
    public void GenerateDisplacementMap_FlatFabric_IsNeutral()
    {
        var photo = Png(200, 160, (_, _) => new Rgba32(200, 200, 200));

        using var displacement = Image.Load<Rgb24>(_generator.GenerateDisplacementMap(photo));

        displacement.Size.Should().Be(new Size(200, 160));
        displacement[100, 80].R.Should().BeInRange(126, 130);
        displacement[100, 80].G.Should().BeInRange(126, 130);
    }

    [TestMethod]
    public void GenerateDisplacementMap_Fold_DrawsThePrintTowardItFromBothSides()
    {
        // A dark 7px vertical fold through light fabric, centered on x = 100.
        var photo = Png(200, 200, (x, _) => x is >= 97 and < 104 ? new Rgba32(120, 120, 120) : new Rgba32(220, 220, 220));

        using var displacement = Image.Load<Rgb24>(_generator.GenerateDisplacementMap(photo));

        displacement[96, 100].R.Should().BeLessThan(100, "left of the fold the print moves right");
        displacement[104, 100].R.Should().BeGreaterThan(156, "right of the fold the print moves left");
        displacement[100, 100].R.Should().BeInRange(126, 130, "the middle of the fold is level");
        displacement[96, 100].G.Should().BeInRange(126, 130, "an upright fold moves nothing up or down");
        displacement[60, 100].R.Should().BeInRange(126, 130, "flat fabric away from the fold stays put");
    }

    [TestMethod]
    public void GenerateDisplacementMap_GarmentEdgeAndBackground_AreNeutral()
    {
        // A light garment on a darker background that has a fold-like stripe of its own.
        var photo = Png(200, 200, (x, y) =>
            x is >= 40 and < 160 && y is >= 40 and < 160 ? new Rgba32(240, 240, 240)
            : x is >= 17 and < 23 ? new Rgba32(20, 20, 20)
            : new Rgba32(128, 128, 128));

        using var displacement = Image.Load<Rgb24>(_generator.GenerateDisplacementMap(photo));

        foreach (var x in new[] { 15, 20, 25, 38, 41, 44 })
            displacement[x, 100].R.Should().BeInRange(126, 130, $"x={x} is off the garment or at its edge");
    }

    [TestMethod]
    public void GenerateDisplacementMap_LargePhoto_KeepsTheOriginalSize()
    {
        var photo = Png(2400, 1800, (_, _) => new Rgba32(180, 180, 180));

        using var displacement = Image.Load<Rgb24>(_generator.GenerateDisplacementMap(photo));

        displacement.Size.Should().Be(new Size(2400, 1800));
    }

    [TestMethod]
    public void GenerateGarmentMask_CarriesTheFabricShadingInItsColor()
    {
        // Light garment with a darker fold, on a mid-gray background.
        var photo = Png(200, 200, (x, y) =>
            x is >= 40 and < 160 && y is >= 40 and < 160
                ? x is >= 97 and < 103 ? new Rgba32(160, 160, 160) : new Rgba32(240, 240, 240)
                : new Rgba32(128, 128, 128));

        using var mask = Image.Load<Rgba32>(_generator.GenerateGarmentMask(photo).MaskPng);

        mask[60, 100].R.Should().BeGreaterThan(245, "flat fabric is the white level");
        mask[100, 100].R.Should().BeLessThan(200, "the fold darkens what is multiplied over it");
        mask[100, 100].A.Should().Be(255);
    }

    [TestMethod]
    public void GenerateGarmentMask_ReportsHowMuchThePhotoTheGarmentCoversAndHowBrightItIs()
    {
        var photo = Png(200, 200, (x, y) =>
            x is >= 40 and < 160 && y is >= 40 and < 160 ? new Rgba32(245, 245, 245) : new Rgba32(128, 128, 128));

        var result = _generator.GenerateGarmentMask(photo);

        using var mask = Image.Load<Rgba32>(result.MaskPng);
        mask[100, 100].A.Should().Be(255);
        mask[10, 10].A.Should().Be(0);
        result.GarmentCoverage.Should().BeApproximately(0.36, 0.01);
        result.GarmentLuminance.Should().BeGreaterThan(0.85);
    }

    [TestMethod]
    public void GenerateGarmentMask_NoGarmentToldApart_CountsTheWholePhotoAsTheGarment()
    {
        // A model that takes only a corner speck of the photo.
        var speck = new FakeGarmentSegmenter((x, y) => x < 0.1 && y < 0.1);
        var photo = Png(200, 200, (_, _) => new Rgba32(240, 240, 240));

        var result = new ImageSharpMockupMapGenerator(speck).GenerateGarmentMask(photo);

        using var mask = Image.Load<Rgba32>(result.MaskPng);
        mask[100, 100].A.Should().Be(255, "a design is not cut to a mask that found no garment");
        result.GarmentCoverage.Should().BeApproximately(0.01, 0.005, "what the model found is still reported");
    }

    [TestMethod]
    public void GenerateGarmentMask_PreviewSize_ScalesTheResultDown()
    {
        var photo = Png(2000, 1000, (x, y) =>
            x is >= 400 and < 1600 && y is >= 200 and < 800 ? new Rgba32(240, 240, 240) : new Rgba32(128, 128, 128));

        using var mask = Image.Load<Rgba32>(_generator.GenerateGarmentMask(photo, maxOutputSide: 500).MaskPng);

        mask.Size.Should().Be(new Size(500, 250));
        mask[250, 125].A.Should().Be(255);
    }

    [TestMethod]
    public void GenerateGarmentMask_OnlyAPreviewAsksTheModelForTheQuickAnswer()
    {
        var model = new FakeGarmentSegmenter((x, y) => x < 0.5);
        var generator = new ImageSharpMockupMapGenerator(model);
        var photo = Png(200, 200, (_, _) => new Rgba32(240, 240, 240));

        generator.GenerateGarmentMask(photo, maxOutputSide: 100);
        generator.GenerateGarmentMask(photo);

        model.QuickCalls.Should().Equal(true, false);
    }

    [TestMethod]
    public void GenerateGarmentMask_DarkGarment_ReportsLowLuminance()
    {
        var photo = Png(200, 200, (x, y) =>
            x is >= 40 and < 160 && y is >= 40 and < 160 ? new Rgba32(25, 25, 40) : new Rgba32(240, 240, 240));

        var result = _generator.GenerateGarmentMask(photo);

        result.GarmentLuminance.Should().BeLessThan(0.2);
    }

    [TestMethod]
    public void GenerateGarmentMask_ModelGivesNoAnswer_Fails()
    {
        var silent = new FakeGarmentSegmenter(null);
        var photo = Png(200, 200, (_, _) => new Rgba32(240, 240, 240));

        var generate = () => new ImageSharpMockupMapGenerator(silent).GenerateGarmentMask(photo);

        generate.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void IsAvailable_FollowsTheModel()
    {
        new ImageSharpMockupMapGenerator(Middle).IsAvailable.Should().BeTrue();
        new ImageSharpMockupMapGenerator(new FakeGarmentSegmenter(null, available: false)).IsAvailable.Should().BeFalse();
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

/// <summary>Stands in for the model: garment wherever <paramref name="isGarment"/> says, by position 0..1.</summary>
internal sealed class FakeGarmentSegmenter(Func<double, double, bool>? isGarment, bool available = true) : IGarmentSegmenter
{
    public bool IsAvailable => available;

    public List<bool> QuickCalls { get; } = [];

    public byte[]? Segment(Image<Rgba32> photo, int width, int height, bool quick = false)
    {
        QuickCalls.Add(quick);
        if (isGarment is null) return null;
        var result = new byte[width * height];
        for (var i = 0; i < result.Length; i++)
            result[i] = isGarment((i % width + 0.5) / width, (i / width + 0.5) / height) ? (byte)255 : (byte)0;
        return result;
    }
}
