using APCS.Application.Abstractions.Storage;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class CloudinaryMockupCompositorTests
{
    private readonly CloudinaryMockupCompositor compositor = new(Microsoft.Extensions.Options.Options.Create(new CloudinaryOptions { Folder = "apcs" }));

    [TestMethod]
    public void BuildCompositeUrl_InsertsTransformationRightAfterUploadMarker()
    {
        var url = compositor.BuildCompositeUrl(
            "https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/mockup-templates/abc.jpg",
            "design-images/prompt-id/0",
            new MockupPosition(820, 640, 900, 1100));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:design-images:prompt-id:0,g_north_west,x_820,y_640,w_900,h_1100,c_fit,fl_layer_apply/"
            + "v1700000000/apcs/mockup-templates/abc.jpg");
    }

    [TestMethod]
    public void BuildCompositeUrl_PrefixesTheOverlayStorageKeyWithTheConfiguredFolder()
    {
        var url = compositor.BuildCompositeUrl(
            "https://res.cloudinary.com/demo/image/upload/base.jpg",
            "a/b/c",
            new MockupPosition(0, 0, 100, 100));

        url.Should().Contain("l_apcs:a:b:c,");
        url.Should().NotContain("l_a:b:c,");
        url.Should().NotContain("l_a/b/c");
    }

    [TestMethod]
    public void BuildCompositeUrl_WhenNoFolderIsConfigured_UsesTheBareStorageKey()
    {
        var bareCompositor = new CloudinaryMockupCompositor(Microsoft.Extensions.Options.Options.Create(new CloudinaryOptions { Folder = "" }));

        var url = bareCompositor.BuildCompositeUrl(
            "https://res.cloudinary.com/demo/image/upload/base.jpg",
            "design-images/prompt-id/0",
            new MockupPosition(0, 0, 100, 100));

        url.Should().Contain("l_design-images:prompt-id:0,");
    }

    [TestMethod]
    public void BuildCompositeUrl_BaseUrlWithoutUploadMarker_Throws()
    {
        var act = () => compositor.BuildCompositeUrl(
            "https://example.com/not-cloudinary.jpg", "overlay", new MockupPosition(0, 0, 10, 10));

        act.Should().Throw<InvalidOperationException>();
    }

    private const string Base = "https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/mockup-templates/t.webp";

    [TestMethod]
    public void BuildCompositeUrl_WithDesignCorners_WarpsTheDesignOntoThemThenBendsAndCutsIt()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(90, 180, 330, 340),
            new MockupLayers(1024, 1024, "mockup-templates/t-displace", "mockup-templates/t-mask", null,
                DesignCorners: [new(100, 200), new(400, 180), new(420, 500), new(90, 520)]));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:design-images:d/c_scale,w_330,h_340/e_distort:10:20:310:0:330:320:0:340/c_crop,g_north_west,x_0,y_0,w_330,h_340/"
            + "l_apcs:mockup-templates:t-displace/c_crop,g_north_west,x_90,y_180,w_330,h_340/e_displace,fl_layer_apply,x_7,y_7/"
            + "l_apcs:mockup-templates:t-mask/c_crop,g_north_west,x_90,y_180,w_330,h_340/fl_cutter,fl_layer_apply,g_north_west/"
            + "fl_layer_apply,g_north_west,x_90,y_180/"
            + "v1700000000/apcs/mockup-templates/t.webp");
    }

    [TestMethod]
    public void BuildCompositeUrl_WithMorePlacements_PrintsTheDesignOnEachAndRecolorsOnce()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(100, 200, 300, 300),
            new MockupLayers(1024, 1024, "mockup-templates/t-displace", "mockup-templates/t-mask", "#1F2A44",
                MorePlacements: [new MockupPosition(600, 260, 200, 200)]));

        CountOf(url, "l_apcs:design-images:d/").Should().Be(2);
        url.Should().Contain("c_scale,w_300,h_300/").And.Contain("fl_layer_apply,g_north_west,x_100,y_200/")
            .And.Contain("c_scale,w_200,h_200/").And.Contain("fl_layer_apply,g_north_west,x_600,y_260/");
        CountOf(url, "e_colorize:100,co_rgb:1F2A44").Should().Be(1);
        CountOf(url, "fl_layer_apply,e_multiply,g_north_west,x_0,y_0").Should().Be(1);
        // The design goes on each shirt before the fabric's shading is multiplied over all of them.
        url.LastIndexOf("l_apcs:design-images:d/", StringComparison.Ordinal)
            .Should().BeLessThan(url.IndexOf("fl_layer_apply,e_multiply,g_north_west,x_0,y_0", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildCompositeUrl_OneStraightPlacement_IsTheSameAsTheDesignAlone()
    {
        var layers = new MockupLayers(1024, 1024, "mockup-templates/t-displace", "mockup-templates/t-mask", "#1F2A44", MultiplyDesign: false);
        var alone = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(100, 200, 300, 400), layers);

        var placed = compositor.BuildCompositeUrl(Base, [new MockupPlacement("design-images/d", new MockupPosition(100, 200, 300, 400), 1024, 1024)], layers);

        placed.Should().Be(alone);
    }

    [TestMethod]
    public void BuildCompositeUrl_TwoDesigns_FitsEachInItsOwnAreaAtItsOwnSizeAndRecolorsOnce()
    {
        var url = compositor.BuildCompositeUrl(Base,
            [
                new MockupPlacement("design-images/front", new MockupPosition(100, 200, 300, 300), 1024, 1024),
                new MockupPlacement("design-images/logo", new MockupPosition(600, 260, 200, 200), 500, 250),
            ],
            new MockupLayers(null, null, "mockup-templates/t-displace", "mockup-templates/t-mask", "#1F2A44"));

        url.Should().Contain("l_apcs:design-images:front/c_scale,w_300,h_300/")
            .And.Contain("fl_layer_apply,g_north_west,x_100,y_200/")
            // The wide logo is fitted to the area's width and centered in its height.
            .And.Contain("l_apcs:design-images:logo/c_scale,w_200,h_100/")
            .And.Contain("fl_layer_apply,g_north_west,x_600,y_310/");
        CountOf(url, "e_colorize:100,co_rgb:1F2A44").Should().Be(1);
        url.IndexOf("design-images:front", StringComparison.Ordinal).Should().BeLessThan(url.IndexOf("design-images:logo", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildCompositeUrl_QuarterTurn_WarpsTheDesignOntoItsTurnedCorners()
    {
        var url = compositor.BuildCompositeUrl(Base,
            [new MockupPlacement("design-images/d", new MockupPosition(100, 200, 300, 300), 1024, 1024, Rotation: 90)],
            new MockupLayers(null, null, "mockup-templates/t-displace", null, null));

        // Turned clockwise: the top-left corner goes to the top right, and so on round the square.
        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:design-images:d/c_scale,w_300,h_300/e_distort:300:0:300:300:0:300:0:0/c_crop,g_north_west,x_0,y_0,w_300,h_300/"
            + "l_apcs:mockup-templates:t-displace/c_crop,g_north_west,x_100,y_200,w_300,h_300/e_displace,fl_layer_apply,x_6,y_6/"
            + "fl_layer_apply,g_north_west,x_100,y_200/"
            + "v1700000000/apcs/mockup-templates/t.webp");
    }

    [TestMethod]
    public void BuildCompositeUrl_SlightTurn_LaysTheDesignInTheBoxAroundItsTurnedCorners()
    {
        var url = compositor.BuildCompositeUrl(Base,
            [new MockupPlacement("design-images/d", new MockupPosition(100, 200, 300, 300), 1024, 1024, Rotation: 30)], null);

        // A 300 px square turned 30 degrees spans 300 * (cos 30 + sin 30) = 410 px, about the same center.
        url.Should().Contain("c_scale,w_410,h_410/e_distort:").And.Contain("fl_layer_apply,g_north_west,x_45,y_145/");
    }

    [TestMethod]
    public void BuildCompositeUrl_NoPlacements_Throws()
    {
        var act = () => compositor.BuildCompositeUrl(Base, Array.Empty<MockupPlacement>());

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void RotatedBounds_StraightOrQuarterTurnOfASquare_IsTheSquare()
    {
        var square = new MockupPosition(100, 200, 300, 300);

        MockupGeometry.RotatedBounds(square, 0).Should().Be(square);
        MockupGeometry.RotatedBounds(square, 90).Should().Be(square);
        MockupGeometry.RotatedBounds(new MockupPosition(0, 0, 200, 100), 90).Should().Be(new MockupPosition(50, -50, 100, 200));
    }

    private static int CountOf(string text, string part) =>
        (text.Length - text.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;

    [TestMethod]
    public void BuildCompositeUrl_WithDesignSizeAndDisplacement_CentersTheDesignAndBendsIt()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(1418, 1475, 1152, 1039),
            new MockupLayers(1024, 1024, "mockup-templates/t-displace", null, null, MultiplyDesign: true));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:design-images:d/c_scale,w_1039,h_1039/"
            + "l_apcs:mockup-templates:t-displace/c_crop,g_north_west,x_1474,y_1475,w_1039,h_1039/e_displace,fl_layer_apply,x_21,y_21/"
            + "fl_layer_apply,e_multiply,g_north_west,x_1474,y_1475/"
            + "v1700000000/apcs/mockup-templates/t.webp");
    }

    [TestMethod]
    public void BuildCompositeUrl_WithABackground_CutsTheGarmentOutShadowsItAndFillsTheRest()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(40, 60, 400, 200),
            new MockupLayers(1000, 1000, null, "mockup-templates/t-mask", null,
                CutMaskKey: "mockup-templates/t-cut", BackgroundColor: "#ede6da", BasePhotoWidthPx: 1500, BasePhotoHeightPx: 2000));

        url.Should().EndWith(
            "fl_layer_apply,g_north_west,x_140,y_60/"
            + "l_apcs:mockup-templates:t-cut/fl_cutter,fl_layer_apply,g_north_west,x_0,y_0/"
            + "u_apcs:mockup-templates:t-cut/e_colorize:100,co_rgb:000000/e_blur:1350/o_38/fl_layer_apply,g_north_west,x_0,y_18/"
            + "c_crop,g_north_west,w_1500,h_2000/b_rgb:EDE6DA/"
            + "v1700000000/apcs/mockup-templates/t.webp");
    }

    [TestMethod]
    [DataRow("mockup-templates/t-cut", null, DisplayName = "No background chosen")]
    [DataRow(null, "#EDE6DA", DisplayName = "No cut mask")]
    public void BuildCompositeUrl_WithoutBothTheCutMaskAndTheColor_KeepsThePhotosBackground(string? cutMaskKey, string? background)
    {
        var plain = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(40, 60, 400, 200),
            new MockupLayers(1000, 1000, null, "mockup-templates/t-mask", null));

        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(40, 60, 400, 200),
            new MockupLayers(1000, 1000, null, "mockup-templates/t-mask", null,
                CutMaskKey: cutMaskKey, BackgroundColor: background, BasePhotoWidthPx: 1500, BasePhotoHeightPx: 2000));

        url.Should().Be(plain);
    }

    [TestMethod]
    [DataRow(1152, 1039, "x_12,y_12", DisplayName = "A large design is bent by no more than the photo's folds allow")]
    [DataRow(300, 300, "x_6,y_6", DisplayName = "A small design is bent by a share of its own size")]
    public void BuildCompositeUrl_WithThePhotoSize_LimitsTheBendOfALargeDesign(int areaWidth, int areaHeight, string expected)
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(100, 100, areaWidth, areaHeight),
            new MockupLayers(1024, 1024, "mockup-templates/t-displace", null, null, BasePhotoShortSidePx: 2000));

        url.Should().Contain($"e_displace,fl_layer_apply,{expected}/");
    }

    [TestMethod]
    public void BuildCompositeUrl_WithGarmentMask_CutsTheBentDesignToTheGarment()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(40, 60, 400, 200),
            new MockupLayers(1000, 1000, "mockup-templates/t-displace", "mockup-templates/t-mask", null));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:design-images:d/c_scale,w_200,h_200/"
            + "l_apcs:mockup-templates:t-displace/c_crop,g_north_west,x_140,y_60,w_200,h_200/e_displace,fl_layer_apply,x_4,y_4/"
            + "l_apcs:mockup-templates:t-mask/c_crop,g_north_west,x_140,y_60,w_200,h_200/fl_cutter,fl_layer_apply,g_north_west/"
            + "fl_layer_apply,g_north_west,x_140,y_60/"
            + "v1700000000/apcs/mockup-templates/t.webp");
    }

    [TestMethod]
    public void BuildCompositeUrl_WithDesignSizeButNoMaps_OnlyPlacesTheDesign()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(0, 0, 400, 200),
            new MockupLayers(1000, 1000, null, null, null));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:design-images:d/c_scale,w_200,h_200/fl_layer_apply,g_north_west,x_100,y_0/"
            + "v1700000000/apcs/mockup-templates/t.webp");
    }

    [TestMethod]
    public void BuildCompositeUrl_WithUnknownDesignSize_FallsBackToThePlainOverlay()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(10, 20, 300, 400),
            new MockupLayers(0, 0, "mockup-templates/t-displace", "mockup-templates/t-mask", null));

        url.Should().Contain("l_apcs:design-images:d,g_north_west,x_10,y_20,w_300,h_400,c_fit,fl_layer_apply/");
        url.Should().NotContain("e_displace");
        url.Should().NotContain("fl_cutter");
    }

    [TestMethod]
    public void BuildCompositeUrl_WithGarmentColor_PaintsTheGarmentThenMultipliesItsShadingOverTheDesign()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(0, 0, 100, 100),
            new MockupLayers(100, 100, null, "mockup-templates/t-mask", "#1f2a44"));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:mockup-templates:t-mask/e_colorize:100,co_rgb:1F2A44/fl_layer_apply,g_north_west,x_0,y_0/"
            + "l_apcs:design-images:d/c_scale,w_100,h_100/"
            + "l_apcs:mockup-templates:t-mask/c_crop,g_north_west,x_0,y_0,w_100,h_100/fl_cutter,fl_layer_apply,g_north_west/"
            + "fl_layer_apply,g_north_west,x_0,y_0/"
            + "l_apcs:mockup-templates:t-mask/fl_layer_apply,e_multiply,g_north_west,x_0,y_0/"
            + "v1700000000/apcs/mockup-templates/t.webp");
    }

    [TestMethod]
    public void FitDesign_LandscapeDesignInSquareArea_IsCenteredVertically()
    {
        var fitted = CloudinaryMockupCompositor.FitDesign(new MockupPosition(100, 100, 500, 500), new MockupLayers(1600, 900, null, null, null));

        fitted.Should().Be(new MockupPosition(100, 209, 500, 281));
    }

    [TestMethod]
    public void BuildAssetUrl_PointsAtTheStorageKeyAsPng()
    {
        compositor.BuildAssetUrl(Base, "mockup-templates/t-mask")
            .Should().Be("https://res.cloudinary.com/demo/image/upload/apcs/mockup-templates/t-mask.png");
    }

    [TestMethod]
    public void AsJpeg_ReplacesTheExtensionOfTheLastPathSegment()
    {
        MockupMapService.AsJpeg(Base).Should().Be("https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/mockup-templates/t.jpg");
        MockupMapService.AsJpeg("https://cdn.example/a.b/photo").Should().Be("https://cdn.example/a.b/photo.jpg");
    }
}
