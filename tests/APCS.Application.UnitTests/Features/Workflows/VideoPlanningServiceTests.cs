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
    public async Task ListAiCharactersAsync_ActiveModels_ReturnsDbOrderedCatalogAndForwardsCancellation()
    {
        var fixture = new WorkflowTestData();
        fixture.CharacterModelRows.AddRange([
            Character("adrian", "Adrian", sortOrder: 20),
            Character("mai", "Mai", sortOrder: 10, isDefault: true),
            Character("deleted", "Deleted", sortOrder: 1, deletedAt: WorkflowTestData.Now.UtcDateTime),
            Character("inactive", "Inactive", sortOrder: 2, isActive: false),
        ]);
        using var cancellation = new CancellationTokenSource();

        var result = await Service(fixture).ListAiCharactersAsync(cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(model => model.Code).Should().Equal("mai", "adrian");
        result.Value[0].Should().Match<AiCharacterCatalogItem>(model =>
            model.IsDefault && model.Name == "Mai" && model.ImageUrl == "https://example.test/mai.png");
        fixture.CharacterModels.Verify(repository => repository.FindAsync(
            It.IsAny<Expression<Func<AiCharacterModel, bool>>>(), cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task ListVideoPacksAsync_ProductOnModelV2_ReturnsThreeOrderedAiScenes()
    {
        var fixture = new WorkflowTestData();
        fixture.TemplateRows.Add(new()
        {
            Id = Guid.NewGuid(), Code = "product_on_model_pack", Name = "Product on Model", Type = "video_pack",
            Platform = "etsy", DurationSeconds = 12, AspectRatio = "9:16", Resolution = "720p", IsSystemTemplate = true,
            IsActive = true, TemplateVersion = 2,
            EffectsConfig = WorkflowJson.Write(new
            {
                videoPack = new
                {
                    description = "Three product shots.",
                    requiresCharacter = true,
                    requiresSharedMockup = true,
                    scenes = new[]
                    {
                        new { id = "natural_motion", mode = "ai", durationSeconds = 4, templateCode = "lifestyle_in_use", subtemplateCode = "natural_motion", transitionIn = "cut", transitionDurationFrames = 0 },
                        new { id = "brand_focus", mode = "ai", durationSeconds = 4, templateCode = "lifestyle_in_use", subtemplateCode = "brand_focus", transitionIn = "cut", transitionDurationFrames = 0 },
                        new { id = "fabric_detail", mode = "ai", durationSeconds = 4, templateCode = "lifestyle_in_use", subtemplateCode = "fabric_detail", transitionIn = "cut", transitionDurationFrames = 0 },
                    },
                },
            }),
        });

        var result = await Service(fixture).ListVideoPacksAsync(default);

        result.IsSuccess.Should().BeTrue();
        var pack = result.Value.Should().ContainSingle().Which;
        pack.Should().Match<VideoPackDefinition>(item =>
            item.Code == "product_on_model_pack" && item.Version == 2 && item.TotalDurationSeconds == 12 &&
            item.RequiresCharacter && item.RequiresSharedMockup);
        pack.Scenes.Select(scene => scene.Id).Should().Equal(
            "natural_motion", "brand_focus", "fabric_detail");
        pack.Scenes.Should().OnlyContain(scene => scene.Mode == "ai" && scene.DurationSeconds == 4 &&
            scene.TemplateCode == "lifestyle_in_use" && scene.TransitionIn == "cut");
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
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 5, AiScenes: [new(MockupId: source.Id)],
            AiStudioMode: "studio", AiStudioSourceMockupId: source.Id, AiStudioFrameId: frame.Id);

        var result = await Service(fixture).PreviewAsync(product.Id, new(config, [source.Id], source.ArtworkGroupKey), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Single().MockupId.Should().Be(frame.Id);
        result.Value.AiShots!.Single().MockupId.Should().Be(frame.Id);
    }

    [TestMethod]
    public async Task PreviewAsync_StandardGeneratedKeyframeRequiresApprovalAndUsesItsCustomFraming()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var frame = WorkflowTestData.Asset(product.Id, "VideoScene");
        frame.SourceType = MockupSources.AiGenerated;
        frame.MetadataRevision = 1;
        frame.ContentHash = new string('b', 64);
        frame.ApprovalStatus = "pending";
        frame.ApprovedRevision = null;
        fixture.AssetRows.Add(frame);
        var startCrop = new Region(.1, .1, .8, .8);
        var endCrop = new Region(.15, .15, .7, .7);
        var config = new GenerateVideoConfig(Template: "product_showcase", TemplateVersion: 2,
            AssetSelection: "manual", SelectedMockupIds: [frame.Id], SceneOrder: [frame.Id],
            StandardScenes: [new(frame.Id, startCrop, endCrop)]);
        var request = new PreviewStoryboardRequest(config, [source.Id], source.ArtworkGroupKey);

        var pending = await Service(fixture).PreviewAsync(product.Id, request, default);
        pending.IsSuccess.Should().BeFalse();
        pending.Error.Code.Should().Be("WaitingForInput");

        frame.ApprovalStatus = "approved";
        frame.ApprovedRevision = frame.MetadataRevision;
        var approved = await Service(fixture).PreviewAsync(product.Id, request, default);

        approved.IsSuccess.Should().BeTrue();
        approved.Value.Scenes.Should().HaveCount(2).And.OnlyContain(scene => scene.MockupId == frame.Id);
        approved.Value.Scenes.Should().OnlyContain(scene => scene.Crop == startCrop && scene.EndCrop == endCrop);
        fixture.JobRows.Should().BeEmpty();
        fixture.VideoRows.Should().BeEmpty();
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

    [TestMethod]
    public async Task PreviewAsync_ForDesign_MovesScenesOfThePreviewProductOntoTheDesignsMockups()
    {
        var fixture = new WorkflowTestData();
        var template = Guid.NewGuid();
        var preview = fixture.AddProduct();
        var design = fixture.AddProduct();
        var previewMockup = fixture.AddAsset(preview); previewMockup.MockupTemplateId = template; previewMockup.VariantKey = "white";
        var designMockup = fixture.AddAsset(design); designMockup.MockupTemplateId = template; designMockup.VariantKey = "white";
        var config = new GenerateVideoConfig(Mode: "standard", DurationSeconds: 6,
            AiScenes: [new(MockupId: previewMockup.Id, Mode: "standard", DurationSeconds: 6, StandardMotionPreset: "zoom")]);
        var groups = WorkflowTestData.Repository(new List<WorkflowRunGroup>(), x => x.Id);
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        var service = new VideoPlanningService(user.Object, fixture.Products.Object, fixture.Assets.Object, fixture.Templates.Object,
            fixture.CharacterModels.Object, new(), new(), new(), targetResolver: new RunTargetConfigResolver(fixture.Assets.Object, fixture.Products.Object, groups.Object));

        var plain = await service.PreviewAsync(design.Id, new(config, [], "artwork-main"), default);
        var forDesign = await service.PreviewAsync(design.Id, new(config, [], "artwork-main", ForDesign: true), default);

        plain.IsFailure.Should().BeTrue();
        forDesign.IsSuccess.Should().BeTrue(forDesign.IsFailure ? forDesign.Error.Message : null);
        forDesign.Value.Scenes.Should().OnlyContain(scene => scene.MockupId == designMockup.Id);
    }

    private static VideoPlanningService Service(WorkflowTestData fixture)
    {
        var user = new Mock<ICurrentUser>(); user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        return new(user.Object, fixture.Products.Object, fixture.Assets.Object, fixture.Templates.Object,
            fixture.CharacterModels.Object, new(), new(), new());
    }

    private static AiCharacterModel Character(string code, string name, int sortOrder, bool isDefault = false,
        bool isActive = true, DateTime? deletedAt = null) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = name, Continent = "Asia", Gender = "Female",
        AgeMin = 18, AgeMax = 24, BodyType = "Slim", SkinTone = "Tan", Description = "Model",
        CloudinaryPublicId = $"ai-characters/{code}", ImageUrl = $"https://example.test/{code}.png",
        PreviewUrl = $"https://example.test/{code}-preview.png", SortOrder = sortOrder,
        IsDefault = isDefault, IsActive = isActive, DeletedAt = deletedAt
    };
}
