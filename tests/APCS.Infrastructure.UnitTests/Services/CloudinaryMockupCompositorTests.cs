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
}
