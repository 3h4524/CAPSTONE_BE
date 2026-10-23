using System.Linq.Expressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.Workflows;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoPlanningServiceTests
{
    [TestMethod]
    public async Task ListTemplatesAsync_ActiveVersionTwoTemplates_ReturnsOrderedCatalogDefaults()
    {
        var fixture = new WorkflowTestData();
        fixture.TemplateRows.Single(x => x.TemplateVersion == 2 && x.Code == "design_detail").PreviewVideoUrl = "/detail.mp4";
        fixture.TemplateRows.Add(new() { Id = Guid.NewGuid(), Code = "unsupported", Name = "Unsupported", Type = "product_showcase",
            Platform = "etsy", DurationSeconds = 12, AspectRatio = "9:16", Resolution = "1080x1920", EffectsConfig = "{}",
            IsSystemTemplate = true, IsActive = true, TemplateVersion = 2 });

        var result = await Service(fixture).ListTemplatesAsync(default);

        result.Value.Select(x => x.Code).Should().Equal("product_showcase", "design_detail", "variant_showcase");
        result.Value.Single(x => x.Code == "design_detail").Should().Match<VideoTemplateCatalogItem>(x =>
            x.Version == 2 && x.PreviewVideoUrl == "/detail.mp4" && x.DefaultDurationSeconds == 12 &&
            // A stored fade (or cut) default is offered as none, the transition new videos start with.
            x.DefaultTransition == "none" && x.DefaultMotionPreset == "varied" &&
            x.Requirements == new VideoTemplateRequirements(1, true, 0));
        result.Value.Single(x => x.Code == "variant_showcase").Requirements.Should().Be(new VideoTemplateRequirements(2, false, 2));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("not-json")]
    [DataRow("")]
    public async Task ListTemplatesAsync_MalformedEffectsConfig_ReturnsCatalogWithFallbackRequirements(string effectsConfig)
    {
        var fixture = new WorkflowTestData();
        fixture.TemplateRows.Single(x => x.TemplateVersion == 2 && x.Code == "product_showcase").EffectsConfig = effectsConfig;

        var result = await Service(fixture).ListTemplatesAsync(default);

        var item = result.Value.Single(x => x.Code == "product_showcase");
        item.Description.Should().NotBeNullOrEmpty();
        item.DefaultTransition.Should().Be("none");
        item.DefaultMotionPreset.Should().Be("varied");
        item.Requirements.Should().Be(new VideoTemplateRequirements(1, false, 0));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PreviewAsync_NullConfigurationOrSelection_ReturnsValidationWithoutAssetQuery(bool nullConfig)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var request = new PreviewStoryboardRequest(nullConfig ? null! : new(), nullConfig ? [] : null!, null);
        var result = await Service(fixture).PreviewAsync(product.Id, request, default);
        result.Error.Message.Should().Be("Video configuration and mockup selection are required.");
        fixture.Assets.Verify(x => x.FindAsync(It.IsAny<Expression<Func<MockupImage, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("foreign")]
    [DataRow("deleted")]
    [DataRow("missing")]
    public async Task PreviewAsync_UnavailableProduct_DoesNotReadOrExposeMockups(string condition)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        if (condition == "foreign") product.UserId = Guid.NewGuid();
        if (condition == "deleted") product.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        if (condition == "missing") fixture.ProductRows.Clear();
        var result = await Service(fixture).PreviewAsync(product.Id, new(new(), [], null), default);
        result.Error.Code.Should().Be("ProductNotFound");
        fixture.Assets.Verify(x => x.FindAsync(It.IsAny<Expression<Func<MockupImage, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.VideoRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PreviewAsync_LegacyAiBackground_RejectsBeforeReadingAssets()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var result = await Service(fixture).PreviewAsync(product.Id, new(new(Mode: "ai_background"), [], null), default);
        result.Error.Code.Should().Be("UnsupportedVideoMode");
        fixture.Assets.Verify(x => x.FindAsync(It.IsAny<Expression<Func<MockupImage, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PreviewAsync_AiShotWithApprovedMockup_ReturnsMixedStoryboardWithoutQueuingJob()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiShotOptions: new(MockupId: source.Id));

        var result = await Service(fixture).PreviewAsync(product.Id, new(config, [source.Id], null), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Mode.Should().Be("ai_shot");
        result.Value.AiShots.Should().ContainSingle().Which.MockupId.Should().Be(source.Id);
        result.Value.Scenes.Count(x => x.GenerationStrategy == "ai_shot").Should().Be(1);
        fixture.JobRows.Should().BeEmpty();
        fixture.VideoRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PreviewAsync_StudioFrameNotInApplyMockupSelection_IsIncludedForShowcaseScenes()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var frame = WorkflowTestData.Asset(product.Id, "VideoScene");
        frame.SourceType = MockupSources.AiGenerated;
        frame.MetadataRevision = 1;
        frame.ApprovedRevision = 1;
        frame.ContentHash = new string('b', 64);
        var aspect = VideoOutputFormats.Resolve(VideoOutputFormats.Default).AspectRatio;
        frame.Regions = AiStudioFrameIdentity.SerializeRegions(new(
            AiStudioFrameIdentity.Fingerprint(source, AiStudioFrameIdentity.HangingWindow, aspect, templateCode: "cinematic_product_showcase"), source.Id,
            source.ContentHash!, source.MetadataRevision, AiStudioFrameIdentity.HangingWindow, aspect, AiStudioFrameIdentity.PromptVersion,
            TemplateCode: "cinematic_product_showcase"));
        fixture.AssetRows.Add(frame);
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 4, AiScenes: [new(MockupId: source.Id)],
            AiStudioMode: "studio", AiStudioSourceMockupId: source.Id, AiStudioFrameId: frame.Id);

        var result = await Service(fixture).PreviewAsync(product.Id, new(config, [source.Id], source.ArtworkGroupKey), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Single().MockupId.Should().Be(frame.Id);
        result.Value.AiShots!.Single().MockupId.Should().Be(frame.Id);
    }

    [TestMethod]
    public async Task PreviewAsync_SelectedApprovedSources_ProducesBoardWithoutMutatingWorkflow()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("mug");
        var selected = fixture.AddAsset(product);
        var rejected = fixture.AddAsset(product); rejected.ApprovalStatus = "rejected";
        var stale = fixture.AddAsset(product); stale.ApprovedRevision = 2;
        var deleted = fixture.AddAsset(product); deleted.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        var otherGroup = fixture.AddAsset(product); otherGroup.ArtworkGroupKey = "other";
        var unselected = fixture.AddAsset(product);
        fixture.AddAsset(fixture.AddProduct());
        using var cancellation = new CancellationTokenSource();
        var result = await Service(fixture).PreviewAsync(product.Id, new(new(), [selected.Id, rejected.Id, stale.Id, deleted.Id, otherGroup.Id], "artwork-main"), cancellation.Token);
        result.IsSuccess.Should().BeTrue();
        result.Value.ProductType.Should().Be("mug");
        result.Value.Template.Should().Be("product_showcase");
        result.Value.Scenes.Should().HaveCount(2).And.OnlyContain(x => x.MockupId == selected.Id && x.SourceRevision == 3);
        result.Value.Scenes.Should().NotContain(x => x.MockupId == unselected.Id);
        fixture.ProductRows.Single(x => x.Id == product.Id).DeletedAt.Should().BeNull();
        fixture.JobRows.Should().BeEmpty();
        fixture.VideoRows.Should().BeEmpty();
        fixture.Products.Verify(x => x.GetByIdAsync(product.Id, cancellation.Token), Times.Once);
        fixture.Assets.Verify(x => x.FindAsync(It.IsAny<Expression<Func<MockupImage, bool>>>(), cancellation.Token), Times.Once);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PreviewAsync_NoCurrentApprovedAsset_ReturnsWaitingForInput()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var pending = fixture.AddAsset(product); pending.ApprovalStatus = "pending";
        var stale = fixture.AddAsset(product); stale.MetadataRevision++;
        var result = await Service(fixture).PreviewAsync(product.Id, new(new(), [], null), default);
        result.Error.Code.Should().Be("WaitingForInput");
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(31, 0)]
    [DataRow(12, 9)]
    public async Task PreviewAsync_InvalidConfigurationOrSelection_RejectsBeforeAssetQuery(int duration, int count)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var request = new PreviewStoryboardRequest(new(DurationSeconds: duration), Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray(), null);
        var result = await Service(fixture).PreviewAsync(product.Id, request, default);
        result.Error.Message.Should().Be("Invalid video configuration or mockup selection.");
        fixture.Assets.Verify(x => x.FindAsync(It.IsAny<Expression<Func<MockupImage, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static VideoPlanningService Service(WorkflowTestData fixture)
    {
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        return new(user.Object, fixture.Products.Object, fixture.Assets.Object, fixture.Templates.Object, new(), new(), new());
    }
}
