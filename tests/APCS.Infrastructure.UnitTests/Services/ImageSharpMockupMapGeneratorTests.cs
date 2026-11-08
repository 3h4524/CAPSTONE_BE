using System.Buffers.Binary;
using System.Text;
using APCS.Infrastructure.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class ImageSharpMockupMapGeneratorTests
{
    // A model that takes the middle of the photo (20%..80% of each side) for the garment.
    private static readonly FakeGarmentSegmenter Middle = new((x, y) => x is >= 0.2 and < 0.8 && y is >= 0.2 and < 0.8);

    private readonly ImageSharpMockupMapGenerator _generator = new(Middle);

    [TestMethod]
    public void GenerateCutMask_PullsTheGarmentsEdgeInByOnePixelAndKeepsItsShading()
    {
        var photo = Png(100, 100, (_, _) => new Rgba32(200, 200, 200));
        var mask = _generator.GenerateGarmentMask(photo).MaskPng;

        using var before = Image.Load<Rgba32>(mask);
        using var cut = Image.Load<Rgba32>(_generator.GenerateCutMask(mask)!);

        cut.Size.Should().Be(before.Size);
        // The garment runs from 20 to 79: its outermost pixel is cut away, the one inside it stays.
        before[20, 50].A.Should().Be(255);
        cut[20, 50].A.Should().Be(0);
        cut[21, 50].A.Should().Be(255);
        cut[79, 50].A.Should().Be(0);
        cut[78, 50].A.Should().Be(255);
        cut[50, 50].R.Should().Be(before[50, 50].R);
    }

    [TestMethod]
    public void GenerateCutMask_WhenNoGarmentWasSeparated_IsNothing()
    {
        var photo = Png(100, 100, (_, _) => new Rgba32(200, 200, 200));
        var everything = new ImageSharpMockupMapGenerator(new FakeGarmentSegmenter((_, _) => true));

        everything.GenerateCutMask(everything.GenerateGarmentMask(photo).MaskPng).Should().BeNull();
    }

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

    // ---- Finding the garment a person wears, with the clothes model ----

    [TestMethod]
    public void GenerateWornGarmentMask_IsThePersonWhereTheClothesModelSeesTheirShirt()
    {
        // The person is the middle of the photo; the clothes model sees a shirt on its top half.
        var photo = Png(100, 100, (_, _) => new Rgba32(230, 230, 230));
        var generator = new ImageSharpMockupMapGenerator(Middle, new FakeClothesSegmenter((_, y) => y < 0.5 ? 0.95 : 0.05));

        using var mask = Image.Load<Rgba32>(generator.GenerateWornGarmentMask(photo)!);

        mask[50, 30].A.Should().Be(255, "the shirt");
        mask[50, 70].A.Should().Be(0, "the person but not their shirt");
        mask[5, 30].A.Should().Be(0, "the shirt's guess off the person");
    }

    [TestMethod]
    public void GenerateWornGarmentMask_FillsAPatchTheModelWasUnsureOfInsideTheShirt()
    {
        var photo = Png(100, 100, (_, _) => new Rgba32(230, 230, 230));
        var unsure = new FakeClothesSegmenter((x, y) => y >= 0.5 ? 0.0 : x is >= 0.45 and < 0.55 && y is >= 0.3 and < 0.4 ? 0.3 : 0.95);
        var generator = new ImageSharpMockupMapGenerator(Middle, unsure);

        using var mask = Image.Load<Rgba32>(generator.GenerateWornGarmentMask(photo)!);

        mask[50, 35].A.Should().BeGreaterThan(200);
    }

    [TestMethod]
    public void GenerateWornGarmentMask_WithoutTheClothesModelOrAShirt_SuggestsNothing()
    {
        var photo = Png(100, 100, (_, _) => new Rgba32(230, 230, 230));

        new ImageSharpMockupMapGenerator(Middle).GenerateWornGarmentMask(photo).Should().BeNull();
        new ImageSharpMockupMapGenerator(Middle, new FakeClothesSegmenter(null)).GenerateWornGarmentMask(photo).Should().BeNull();
        new ImageSharpMockupMapGenerator(Middle, new FakeClothesSegmenter((_, _) => 0.02)).GenerateWornGarmentMask(photo).Should().BeNull();
    }

    [TestMethod]
    public void FillEnclosedGaps_FillsASmallHoleButNotAGapOpenToTheEdgeOrALargeOne()
    {
        // A 20 x 20 garment block with a 2 x 2 hole, a notch open to the photo's edge, and a large enclosed gap.
        var garment = new double[40 * 40];
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 40; x++)
                garment[y * 40 + x] = x is >= 5 and < 35 && y is >= 5 and < 35 ? 1 : 0;
        foreach (var (x, y) in new[] { (10, 10), (11, 10), (10, 11), (11, 11) }) garment[y * 40 + x] = 0;
        for (var y = 18; y < 32; y++) for (var x = 18; x < 32; x++) garment[y * 40 + x] = 0;

        var filled = ImageSharpMockupMapGenerator.FillEnclosedGaps(garment, 40, 40);

        filled[10 * 40 + 10].Should().Be(1, "the small hole is filled");
        filled[25 * 40 + 25].Should().Be(0, "a gap that large is something in front of the garment");
        filled[2 * 40 + 2].Should().Be(0, "outside the garment stays outside");
    }

    // ---- Measuring a garment, to place a design on a model photo as on a mock-up ----

    [TestMethod]
    public void MeasureGarment_FindsTheTopAndTheBodyBelowTheSleeves()
    {
        // A tee in a 100 x 100 mask: rows 10..89; sleeves (columns 10..89) on rows 10..34, body (30..69) below.
        var alpha = Alpha(100, 100, (x, y) => y is >= 10 and < 90 && (y < 35 ? x is >= 10 and < 90 : x is >= 30 and < 70));

        var measured = ImageMaskOps.MeasureGarment(alpha, 100, 100);

        measured.Should().NotBeNull();
        measured!.Top.Should().BeApproximately(0.10, 0.001);
        measured.Bottom.Should().BeApproximately(0.90, 0.001);
        measured.CenterX.Should().BeApproximately(0.50, 0.001);
        measured.BodyWidth.Should().BeApproximately(0.40, 0.001);
    }

    [TestMethod]
    public void MeasureGarments_SeveralPeopleStandingApart_MeasuresEachShirtFromLeftToRight()
    {
        // Three shirts in a 300 x 100 mask (a child's smaller one in the middle) and a speck that is no garment.
        var alpha = Alpha(300, 100, (x, y) =>
            (y is >= 10 and < 90 && (x is >= 200 and < 260 || x is >= 20 and < 80))
            || (y is >= 40 and < 90 && x is >= 120 and < 160)
            || (y is >= 2 and < 4 && x is >= 290 and < 292));

        var measured = ImageMaskOps.MeasureGarments(alpha, 300, 100);

        measured.Should().HaveCount(3);
        measured.Select(garment => garment.CenterX).Should().BeInAscendingOrder();
        measured[0].CenterX.Should().BeApproximately(50 / 300.0, 0.001);
        measured[1].Top.Should().BeApproximately(0.40, 0.001);
        measured[1].BodyWidth.Should().BeApproximately(40 / 300.0, 0.001);
        measured[2].BodyWidth.Should().BeApproximately(60 / 300.0, 0.001);
    }

    [TestMethod]
    public void MeasureGarments_ShirtsThatTouch_CountAsOne()
    {
        var alpha = Alpha(200, 100, (x, y) => y is >= 10 and < 90 && x is >= 20 and < 180);

        ImageMaskOps.MeasureGarments(alpha, 200, 100).Should().ContainSingle();
        ImageMaskOps.MeasureGarments(new byte[200 * 100], 200, 100).Should().BeEmpty();
    }

    [TestMethod]
    public void MeasureGarment_HandAcrossTheBody_KeepsTheBodysEdges()
    {
        var alpha = Alpha(100, 100, (x, y) => y is >= 10 and < 90 && x is >= 30 and < 70 && !(y is >= 50 and < 70 && x is >= 45 and < 55));

        ImageMaskOps.MeasureGarment(alpha, 100, 100)!.BodyWidth.Should().BeApproximately(0.40, 0.001);
    }

    [TestMethod]
    public void MeasureGarment_MaskOfTheWholePhotoOrNothing_MeasuresNothing()
    {
        ImageMaskOps.MeasureGarment(Alpha(50, 50, (_, _) => true), 50, 50).Should().BeNull();
        ImageMaskOps.MeasureGarment(Alpha(50, 50, (_, _) => false), 50, 50).Should().BeNull();
    }

    [TestMethod]
    public void GarmentColor_IsTheWellLitFabricNotItsShadowsOrHighlights()
    {
        // A red tee in a 100 x 100 photo: mostly lit fabric, a band of shadow, a few highlights, gray around it.
        var pixels = new Rgba32[100 * 100];
        var alpha = new byte[pixels.Length];
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var i = y * 100 + x;
                var onGarment = x is >= 20 and < 80 && y is >= 20 and < 80;
                alpha[i] = onGarment ? (byte)255 : (byte)0;
                pixels[i] = !onGarment ? new Rgba32(128, 128, 128)
                    : y < 35 ? new Rgba32(90, 20, 20)
                    : x == 50 ? new Rgba32(255, 230, 230)
                    : new Rgba32(200, 40, 40);
            }
        }

        ImageMaskOps.GarmentColor(pixels, alpha).Should().Be("#C82828");
    }

    [TestMethod]
    public void GarmentColor_MaskOfTheWholePhotoOrNothing_ReadsNothing()
    {
        var pixels = Enumerable.Repeat(new Rgba32(200, 40, 40), 400).ToArray();

        ImageMaskOps.GarmentColor(pixels, Enumerable.Repeat((byte)255, 400).ToArray()).Should().BeNull();
        ImageMaskOps.GarmentColor(pixels, new byte[400]).Should().BeNull();
    }

    [TestMethod]
    public void Limited_AsksTheCdnForASmallerCopy()
    {
        MockupMapService.Limited("https://res.cloudinary.com/demo/image/upload/apcs/mask.png", 800)
            .Should().Be("https://res.cloudinary.com/demo/image/upload/c_limit,w_800,h_800/apcs/mask.png");
        MockupMapService.Limited("https://example.com/mask.png", 800).Should().Be("https://example.com/mask.png");
    }

    private static byte[] Alpha(int width, int height, Func<int, int, bool> garment)
    {
        var alpha = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                alpha[y * width + x] = garment(x, y) ? (byte)255 : (byte)0;
        return alpha;
    }

    // ---- Moving a rough garment outline onto the photo's own edge ----

    [TestMethod]
    public void SnapGarmentEdge_MovesAnOutlineThatIsOffOntoWhereTheShirtMeetsTheSkin()
    {
        // A white shirt above row 100 and skin below it, all of it the person. An outline a few rows into the skin
        // would color it; one a few rows short would leave a white line.
        const int size = 200;
        var pixels = Enumerable.Range(0, size * size)
            .Select(i => i / size < 100 ? new Rgba32(235, 236, 240) : new Rgba32(205, 150, 130)).ToArray();
        var person = Enumerable.Repeat((byte)255, pixels.Length).ToArray();
        float[] OutlineAt(int row) => Enumerable.Range(0, pixels.Length).Select(i => i / size < row ? 1f : 0f).ToArray();

        foreach (var row in new[] { 97, 103 })
        {
            var garment = ImageMaskOps.SnapGarmentEdge(pixels, OutlineAt(row), person, size, size);

            garment[99 * size + 100].Should().Be(1, $"the shirt's last row (outline at {row})");
            garment[101 * size + 100].Should().Be(0, $"the skin below the shirt (outline at {row})");
        }
    }

    [TestMethod]
    public void SnapGarmentEdge_ADarkEdgeAgainstTheShirtGoesWithTheShirt()
    {
        // White shirt, one row of gray where it meets black trousers: recolored whole it stays dark under the
        // fabric's shading; left out, it would show as a pale line between the new color and the trousers.
        const int size = 200;
        var pixels = Enumerable.Range(0, size * size)
            .Select(i => (i / size) switch { < 100 => new Rgba32(235, 236, 240), 100 => new Rgba32(96, 96, 106), _ => new Rgba32(25, 25, 32) })
            .ToArray();
        var person = Enumerable.Repeat((byte)255, pixels.Length).ToArray();
        var outline = Enumerable.Range(0, pixels.Length).Select(i => i / size < 98 ? 1f : 0f).ToArray();

        var garment = ImageMaskOps.SnapGarmentEdge(pixels, outline, person, size, size);

        garment[100 * size + 100].Should().Be(1);
        garment[102 * size + 100].Should().Be(0);
    }

    [TestMethod]
    public void SnapGarmentEdge_LeavesTheEdgeAgainstTheBackgroundToThePersonsMatte()
    {
        // A white shirt against a white wall: color can't tell them apart, so the outline is kept whole up to the
        // person's own edge (x = 100), where the matte cuts it once multiplied in.
        const int size = 200;
        var pixels = Enumerable.Repeat(new Rgba32(240, 240, 242), size * size).ToArray();
        var person = Enumerable.Range(0, pixels.Length).Select(i => i % size < 100 ? (byte)255 : (byte)0).ToArray();
        var outline = Enumerable.Range(0, pixels.Length).Select(i => i % size < 97 ? 1f : 0f).ToArray();

        var garment = ImageMaskOps.SnapGarmentEdge(pixels, outline, person, size, size);

        garment[100 * size + 99].Should().Be(1);
        garment[100 * size + 50].Should().Be(1);
    }

    [TestMethod]
    public void MendPersonMatte_PutsBackAPieceOfAWhiteShirtTheMatteCutOutButNotSkin()
    {
        // A white shirt on a white wall, with a patch of skin; the matte lost a notch of the shirt and the skin
        // patch, and the clothes model is sure of both being the shirt.
        const int size = 100;
        var pixels = Enumerable.Range(0, size * size)
            .Select(i => i % size >= 60 ? new Rgba32(205, 150, 130) : new Rgba32(240, 240, 242)).ToArray();
        var person = Enumerable.Range(0, size * size)
            .Select(i => i % size is >= 20 and < 30 || i % size >= 60 ? (byte)0 : (byte)255).ToArray();
        var upper = Enumerable.Repeat((byte)255, size * size).ToArray();

        var mended = ImageMaskOps.MendPersonMatte(pixels, person, upper);

        mended[50 * size + 25].Should().Be(255, "the shirt's color, where the clothes model is sure");
        mended[50 * size + 70].Should().Be(0, "skin is not the shirt's color");
    }

    // ---- Model photos: the garment is the selected part of the person ----

    [TestMethod]
    public void GenerateGarmentMask_WithASelection_KeepsOnlyTheSelectedPartOfThePerson()
    {
        // The model finds the person in the middle; the seller selected the top half of them (the shirt).
        var photo = Png(100, 100, (_, _) => new Rgba32(230, 230, 230));
        var selection = Selection(100, 100, (_, y) => y < 50);

        var result = _generator.GenerateGarmentMask(photo, garmentSelection: selection);

        using var mask = Image.Load<Rgba32>(result.MaskPng);
        mask[50, 30].A.Should().Be(255, "the shirt is the person and selected");
        mask[50, 70].A.Should().Be(0, "the legs are the person but not selected");
        mask[5, 30].A.Should().Be(0, "the background is selected but not the person");
        result.GarmentCoverage.Should().BeApproximately(0.18, 0.01);
    }

    [TestMethod]
    public void GenerateGarmentMask_WithASelection_TakesTheWhiteLevelFromTheGarmentAlone()
    {
        // A light-gray shirt above brighter skin: the shirt's own brightness is its white level.
        var photo = Png(100, 100, (_, y) => y < 50 ? new Rgba32(200, 200, 200) : new Rgba32(250, 250, 250));
        var selection = Selection(100, 100, (_, y) => y < 50);

        using var mask = Image.Load<Rgba32>(_generator.GenerateGarmentMask(photo, garmentSelection: selection).MaskPng);

        mask[50, 30].R.Should().BeGreaterThan(245);
    }

    [TestMethod]
    public void GenerateGarmentMask_WithASmallSelection_KeepsItInsteadOfTakingTheWholePhoto()
    {
        var photo = Png(100, 100, (_, _) => new Rgba32(230, 230, 230));
        var selection = Selection(100, 100, (x, y) => x is >= 40 and < 50 && y is >= 40 and < 50);

        var result = _generator.GenerateGarmentMask(photo, garmentSelection: selection);

        using var mask = Image.Load<Rgba32>(result.MaskPng);
        mask[45, 45].A.Should().Be(255);
        mask[70, 70].A.Should().Be(0);
        result.GarmentCoverage.Should().BeApproximately(0.01, 0.002);
    }

    [TestMethod]
    public void GenerateGarmentMask_SelectionDrawnOnASmallerCopy_IsScaledToThePhoto()
    {
        var photo = Png(200, 200, (_, _) => new Rgba32(230, 230, 230));
        var selection = Selection(50, 50, (_, y) => y < 25);

        using var mask = Image.Load<Rgba32>(_generator.GenerateGarmentMask(photo, garmentSelection: selection).MaskPng);

        mask.Size.Should().Be(new Size(200, 200));
        mask[100, 60].A.Should().Be(255);
        mask[100, 140].A.Should().Be(0);
    }

    [TestMethod]
    public void GenerateDisplacementMap_WithASelection_DoesNotMoveThePrintOffTheGarment()
    {
        // A crease runs down the legs, which are not selected; the shirt above is flat.
        var photo = Png(200, 200, (x, y) => y >= 100 && x is >= 97 and < 103 ? new Rgba32(120, 120, 120) : new Rgba32(230, 230, 230));
        var selection = Selection(200, 200, (_, y) => y < 100);

        using var displacement = Image.Load<Rgb24>(_generator.GenerateDisplacementMap(photo, selection));

        displacement[95, 150].R.Should().Be(128);
        displacement[100, 60].R.Should().BeInRange(126, 130);
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

    [TestMethod]
    [DataRow("jpeg")]
    [DataRow("webp")]
    public void GenerateGarmentMask_JpegOrWebpPhoto_IsRead(string format)
    {
        var photo = Encoded(format, 200, 200);

        var result = _generator.GenerateGarmentMask(photo);

        (result.WidthPx, result.HeightPx).Should().Be((200, 200));
        _generator.GenerateDisplacementMap(photo).Should().NotBeEmpty();
    }

    [TestMethod]
    [DataRow("tiff")]
    [DataRow("bmp")]
    [DataRow("gif")]
    public void GenerateMaps_PhotoInAFormatThatIsNotRead_IsRefused(string format)
    {
        var photo = Encoded(format, 120, 120);

        var mask = () => _generator.GenerateGarmentMask(photo);
        var displacement = () => _generator.GenerateDisplacementMap(photo);

        mask.Should().Throw<UnknownImageFormatException>("an uploaded file is never handed to that decoder");
        displacement.Should().Throw<UnknownImageFormatException>();
    }

    [TestMethod]
    public void GenerateMaps_FileThatDeclaresAHugePicture_IsRefusedBeforeItIsDecoded()
    {
        // Fifty megapixels in under sixty bytes: only the header is real, so decoding would allocate 200 MB.
        var photo = PngHeaderOnly(10_000, 5_000);

        var mask = () => _generator.GenerateGarmentMask(photo);
        var displacement = () => _generator.GenerateDisplacementMap(photo);

        mask.Should().Throw<InvalidOperationException>().WithMessage("*10000 x 5000*");
        displacement.Should().Throw<InvalidOperationException>();
    }

    // A selection as the editor exports it: opaque where selected, transparent elsewhere.
    private static byte[] Selection(int width, int height, Func<int, int, bool> selected) =>
        Png(width, height, (x, y) => selected(x, y) ? new Rgba32(255, 255, 255, 255) : new Rgba32(0, 0, 0, 0));

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

    // A light-gray photo in the given format.
    internal static byte[] Encoded(string format, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(230, 230, 230));
        IImageEncoder encoder = format switch
        {
            "jpeg" => new JpegEncoder(),
            "webp" => new WebpEncoder(),
            "tiff" => new TiffEncoder(),
            "bmp" => new BmpEncoder(),
            "gif" => new GifEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        using var output = new MemoryStream();
        image.Save(output, encoder);
        return output.ToArray();
    }

    // A PNG that is only its header and end marker: it declares a size without holding any pixels.
    private static byte[] PngHeaderOnly(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typed));
        output.Write(number);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}

/// <summary>Stands in for the clothes model: the chance of a shirt at each position 0..1; null is no model.</summary>
internal sealed class FakeClothesSegmenter(Func<double, double, double>? shirt) : IClothesSegmenter
{
    public bool IsAvailable => shirt is not null;

    public byte[]? UpperClothes(Image<Rgba32> photo, int width, int height)
    {
        if (shirt is null) return null;
        var result = new byte[width * height];
        for (var i = 0; i < result.Length; i++)
            result[i] = (byte)Math.Round(255 * shirt((i % width + 0.5) / width, (i / width + 0.5) / height));
        return result;
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
