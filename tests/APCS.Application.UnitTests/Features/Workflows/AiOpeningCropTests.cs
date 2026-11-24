using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiOpeningCropTests
{
    private const int Width = 1728;
    private const int Height = 2304;
    private static readonly Region MediumShotGarment = new(0.25, 0.3, 0.5, 0.4);

    [TestMethod]
    public void For_FabricDetail_CropsUpperChestInVideoRatio()
    {
        var crop = AiOpeningCrop.For("fabric_detail", MediumShotGarment, Width, Height, "3:4");

        crop.Should().NotBeNull();
        crop!.X.Should().BeApproximately(0.325, 0.0002);
        crop.Y.Should().BeApproximately(0.245, 0.0002);
        crop.Width.Should().BeApproximately(0.35, 0.0002);
        crop.Height.Should().BeApproximately(0.35, 0.0002);
        (crop.Width * Width / (crop.Height * Height)).Should().BeApproximately(3d / 4, 0.002);
    }

    [TestMethod]
    public void For_BrandFocusWithSmallGarment_CropsWholeGarmentWithShoulders()
    {
        var crop = AiOpeningCrop.For("brand_focus", MediumShotGarment, Width, Height, "3:4");

        crop.Should().NotBeNull();
        crop!.X.Should().BeApproximately(0.1625, 0.0002);
        crop.Y.Should().BeApproximately(0.1305, 0.0002);
        crop.Width.Should().BeApproximately(0.675, 0.0002);
        crop.Height.Should().BeApproximately(0.675, 0.0002);
        // Wider than the garment on both sides, and starting above it for the shoulders and neck.
        crop.X.Should().BeLessThan(MediumShotGarment.X);
        crop.Y.Should().BeLessThan(MediumShotGarment.Y);
    }

    [TestMethod]
    public void For_BrandFocusWithLargeGarment_KeepsWholeImage()
    {
        var crop = AiOpeningCrop.For("brand_focus", new Region(0.1, 0.2, 0.8, 0.6), Width, Height, "3:4");

        crop.Should().BeNull();
    }

    [TestMethod]
    public void For_CropBelowMinimumSide_KeepsWholeImage()
    {
        var crop = AiOpeningCrop.For("fabric_detail", new Region(0.4, 0.4, 0.2, 0.2), Width, Height, "3:4");

        crop.Should().BeNull();
    }

    [TestMethod]
    [DataRow("natural_motion")]
    [DataRow("profile_tracking")]
    [DataRow(null)]
    public void For_OtherSubtemplates_KeepWholeImage(string? subtemplate)
    {
        AiOpeningCrop.For(subtemplate, MediumShotGarment, Width, Height, "3:4").Should().BeNull();
    }

    [TestMethod]
    public void For_GarmentAtImageEdge_StaysInsideImage()
    {
        var crop = AiOpeningCrop.For("fabric_detail", new Region(0.5, 0, 0.5, 0.5), Width, Height, "9:16");

        crop.Should().NotBeNull();
        crop!.X.Should().BeGreaterThanOrEqualTo(0);
        crop.Y.Should().BeGreaterThanOrEqualTo(0);
        (crop.X + crop.Width).Should().BeLessThanOrEqualTo(1);
        (crop.Y + crop.Height).Should().BeLessThanOrEqualTo(1);
        (crop.Width * Width / (crop.Height * Height)).Should().BeApproximately(9d / 16, 0.002);
    }

    [TestMethod]
    [DataRow("tshirt", WornGarment.UpperBody)]
    [DataRow("hoodie", WornGarment.UpperBody)]
    [DataRow("tote_bag", WornGarment.Bag)]
    public void GarmentOf_WornProducts_ReturnsGarment(string productType, WornGarment expected)
    {
        AiOpeningCrop.GarmentOf(productType).Should().Be(expected);
    }

    [TestMethod]
    [DataRow("mug")]
    [DataRow("poster")]
    [DataRow("phone_case")]
    public void GarmentOf_ProductsNotWorn_ReturnsNull(string productType)
    {
        AiOpeningCrop.GarmentOf(productType).Should().BeNull();
    }

    [TestMethod]
    public async Task LocateGarmentsAsync_FabricDetailScene_LocatesItsImageWithCallerToken()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("tshirt");
        var source = fixture.AddAsset(product, "Lifestyle");
        var other = fixture.AddAsset(product, "Lifestyle");
        var locator = new Mock<IGarmentLocator>();
        using var cancellation = new CancellationTokenSource();
        locator.Setup(x => x.LocateAsync(source, WornGarment.UpperBody, cancellation.Token)).ReturnsAsync(MediumShotGarment);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes:
        [
            new(MockupId: source.Id, TemplateCode: "lifestyle_in_use", SubtemplateCode: "fabric_detail"),
            new(MockupId: other.Id, TemplateCode: "lifestyle_in_use", SubtemplateCode: "natural_motion")
        ]);

        var boxes = await AiOpeningCrop.LocateGarmentsAsync(locator.Object, product, config, [source, other], cancellation.Token);

        boxes.Should().NotBeNull();
        boxes!.Should().ContainSingle().Which.Should().Be(new KeyValuePair<Guid, Region>(source.Id, MediumShotGarment));
        locator.Verify(x => x.LocateAsync(It.IsAny<MockupImage>(), It.IsAny<WornGarment>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task LocateGarmentsAsync_ProductNotWorn_DoesNotRunClothesModel()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("mug");
        var source = fixture.AddAsset(product, "Lifestyle");
        var locator = new Mock<IGarmentLocator>(MockBehavior.Strict);
        var config = new GenerateVideoConfig(Mode: "ai_shot",
            AiScenes: [new(MockupId: source.Id, TemplateCode: "lifestyle_in_use", SubtemplateCode: "fabric_detail")]);

        var boxes = await AiOpeningCrop.LocateGarmentsAsync(locator.Object, product, config, [source], CancellationToken.None);

        boxes.Should().BeNull();
    }

    [TestMethod]
    [DataRow("studio", null)]
    [DataRow(null, "Show the collar stitching.")]
    public async Task LocateGarmentsAsync_StudioFrameOrCustomPrompt_DoesNotRunClothesModel(string? startFrameMode, string? customPrompt)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("tshirt");
        var source = fixture.AddAsset(product, "Lifestyle");
        var locator = new Mock<IGarmentLocator>(MockBehavior.Strict);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes:
        [
            new(MockupId: source.Id, TemplateCode: "lifestyle_in_use", SubtemplateCode: "brand_focus",
                StartFrameMode: startFrameMode, CustomPrompt: customPrompt)
        ]);

        var boxes = await AiOpeningCrop.LocateGarmentsAsync(locator.Object, product, config, [source], CancellationToken.None);

        boxes.Should().BeNull();
    }
}
