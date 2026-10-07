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
    public void BuildCompositeUrl_WithDesignSizeAndDisplacement_CentersTheDesignAndBendsIt()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(1418, 1475, 1152, 1039),
            new MockupLayers(1024, 1024, "mockup-templates/t-displace", null, null, MultiplyDesign: true));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:design-images:d/c_scale,w_1039,h_1039/"
            + "l_apcs:mockup-templates:t-displace/c_crop,g_north_west,x_1474,y_1475,w_1039,h_1039/e_displace,fl_layer_apply,x_52,y_52/"
            + "fl_layer_apply,e_multiply,g_north_west,x_1474,y_1475/"
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
            new MockupLayers(0, 0, "mockup-templates/t-displace", null, null));

        url.Should().Contain("l_apcs:design-images:d,g_north_west,x_10,y_20,w_300,h_400,c_fit,fl_layer_apply/");
        url.Should().NotContain("e_displace");
    }

    [TestMethod]
    public void BuildCompositeUrl_WithGarmentColor_PaintsTheGarmentThenMultipliesItsShadingOverTheDesign()
    {
        var url = compositor.BuildCompositeUrl(Base, "design-images/d", new MockupPosition(0, 0, 100, 100),
            new MockupLayers(100, 100, null, "mockup-templates/t-mask", "#1f2a44"));

        url.Should().Be(
            "https://res.cloudinary.com/demo/image/upload/"
            + "l_apcs:mockup-templates:t-mask/e_colorize:100,co_rgb:1F2A44/fl_layer_apply,g_north_west,x_0,y_0/"
            + "l_apcs:design-images:d/c_scale,w_100,h_100/fl_layer_apply,g_north_west,x_0,y_0/"
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
