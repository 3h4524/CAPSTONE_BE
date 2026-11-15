using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class SceneSourceResolverTests
{
    private static readonly Guid Front = Guid.NewGuid();
    private static readonly Guid Back = Guid.NewGuid();
    private static readonly Guid PreviewProduct = Guid.NewGuid();
    private static readonly Guid TargetProduct = Guid.NewGuid();

    [TestMethod]
    public void Resolve_SceneFromPreviewProduct_UsesTheTargetsMockupOfTheSameTemplateAndColor()
    {
        var previewFront = Mockup(PreviewProduct, Front, "black");
        var targetWhite = Mockup(TargetProduct, Front, "white");
        var targetBlack = Mockup(TargetProduct, Front, "black");

        var result = SceneSourceResolver.Resolve(Config(Scene(previewFront.Id)), [targetWhite, targetBlack], Referenced(previewFront));

        result.Error.Should().BeNull();
        result.Config.AiScenes![0].MockupId.Should().Be(targetBlack.Id);
        result.Scenes.Single().Warning.Should().BeNull();
    }

    [TestMethod]
    public void Resolve_MockupOfTheTarget_IsKept()
    {
        var own = Mockup(TargetProduct, Back, "white");
        var result = SceneSourceResolver.Resolve(Config(Scene(own.Id)), [Mockup(TargetProduct, Front, "white"), own], new Dictionary<Guid, MockupImage>());
        result.Config.AiScenes![0].MockupId.Should().Be(own.Id);
    }

    [TestMethod]
    public void Resolve_TemplateMissingOnTarget_FallsBackToTheMainMockupWithAWarning()
    {
        var previewBack = Mockup(PreviewProduct, Back, "white");
        var targetFront = Mockup(TargetProduct, Front, "white");

        var result = SceneSourceResolver.Resolve(Config(Scene(previewBack.Id)), [targetFront], Referenced(previewBack));

        result.Error.Should().BeNull();
        result.Config.AiScenes![0].MockupId.Should().Be(targetFront.Id);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("main mock-up");
    }

    [TestMethod]
    public void Resolve_SkipPolicy_LeavesTheSceneOutAndShortensTheVideo()
    {
        var previewFront = Mockup(PreviewProduct, Front, "white");
        var previewBack = Mockup(PreviewProduct, Back, "white");
        var targetFront = Mockup(TargetProduct, Front, "white");
        var config = Config(Scene(previewFront.Id, 6), Scene(previewBack.Id, 6)) with { DurationSeconds = 12, MissingSourcePolicy = "skip" };

        var result = SceneSourceResolver.Resolve(config, [targetFront], Referenced(previewFront, previewBack));

        result.Error.Should().BeNull();
        result.Config.AiScenes.Should().ContainSingle().Which.MockupId.Should().Be(targetFront.Id);
        result.Config.DurationSeconds.Should().Be(6);
        result.Scenes.Should().Contain(scene => scene.SceneIndex == 1 && scene.Skipped);
    }

    [TestMethod]
    public void Resolve_SkipLeavingLessThanFiveSeconds_CannotRender()
    {
        var previewFront = Mockup(PreviewProduct, Front, "white");
        var previewBack = Mockup(PreviewProduct, Back, "white");
        var config = Config(Scene(previewFront.Id, 3), Scene(previewBack.Id, 3)) with { DurationSeconds = 6, MissingSourcePolicy = "skip" };

        var result = SceneSourceResolver.Resolve(config, [Mockup(TargetProduct, Front, "white")], Referenced(previewFront, previewBack));

        result.Error.Should().Contain("at least 5 s");
    }

    [TestMethod]
    public void Resolve_PickedImage_WinsOverTheSharedScene()
    {
        var previewFront = Mockup(PreviewProduct, Front, "white");
        var targetFront = Mockup(TargetProduct, Front, "white");
        var targetBack = Mockup(TargetProduct, Back, "white");

        var result = SceneSourceResolver.Resolve(Config(Scene(previewFront.Id)), [targetFront, targetBack], Referenced(previewFront),
            new TargetOverride([new SceneMockupOverride(0, targetBack.Id)]));

        result.Config.AiScenes![0].MockupId.Should().Be(targetBack.Id);
        result.Scenes.Single().Overridden.Should().BeTrue();
    }

    [TestMethod]
    public void Resolve_SourceTemplateWithColor_PicksThatColorOrWarns()
    {
        var targetWhite = Mockup(TargetProduct, Front, "white");
        var config = Config(Scene(null) with { Source = new SceneSource("template", Front, "black") });

        var result = SceneSourceResolver.Resolve(config, [targetWhite], new Dictionary<Guid, MockupImage>());

        result.Config.AiScenes![0].MockupId.Should().Be(targetWhite.Id);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("black");
    }

    [TestMethod]
    public void Resolve_SameAsEarlierScene_ReusesItsImage()
    {
        var previewBack = Mockup(PreviewProduct, Back, "white");
        var targetBack = Mockup(TargetProduct, Back, "white");
        var config = Config(Scene(previewBack.Id), Scene(null) with { Source = new SceneSource("same_as", RefScene: 0) });

        var result = SceneSourceResolver.Resolve(config, [Mockup(TargetProduct, Front, "white"), targetBack], Referenced(previewBack));

        result.Config.AiScenes!.Select(scene => scene.MockupId).Should().Equal(targetBack.Id, targetBack.Id);
    }

    [TestMethod]
    public void Resolve_KeyframeOfAnotherProduct_StartsFromTheMockup()
    {
        var previewFront = Mockup(PreviewProduct, Front, "white");
        var targetFront = Mockup(TargetProduct, Front, "white");
        var config = Config(Scene(previewFront.Id) with { StartFrameMode = "studio", StudioFrameId = Guid.NewGuid() });

        var result = SceneSourceResolver.Resolve(config, [targetFront], Referenced(previewFront));

        result.Config.AiScenes![0].StartFrameMode.Should().Be("direct");
        result.Config.AiScenes[0].StudioFrameId.Should().BeNull();
    }

    [TestMethod]
    public void Resolve_ManualSelectionOfAnotherProduct_TranslatesOrFallsBackToAutomatic()
    {
        var previewFront = Mockup(PreviewProduct, Front, "white");
        var unknown = Guid.NewGuid();
        var targetFront = Mockup(TargetProduct, Front, "white");

        var translated = SceneSourceResolver.Resolve(new GenerateVideoConfig(AssetSelection: "manual", SelectedMockupIds: [previewFront.Id]),
            [targetFront], Referenced(previewFront));
        var dropped = SceneSourceResolver.Resolve(new GenerateVideoConfig(AssetSelection: "manual", SelectedMockupIds: [unknown]),
            [targetFront], new Dictionary<Guid, MockupImage>());

        translated.Config.SelectedMockupIds.Should().Equal(targetFront.Id);
        dropped.Config.AssetSelection.Should().Be("automatic");
        dropped.Config.SelectedMockupIds.Should().BeNull();
    }

    [TestMethod]
    public void Resolve_NoApprovedMockup_CannotRender()
    {
        var result = SceneSourceResolver.Resolve(new GenerateVideoConfig(), [], new Dictionary<Guid, MockupImage>());
        result.Error.Should().NotBeNull();
    }

    [TestMethod]
    public void Resolve_NoSceneList_MakesOneSceneOnTheMainMockupAsLongAsTheVideo()
    {
        var targetBlack = Mockup(TargetProduct, Front, "#1A1A1A");
        var targetWhite = Mockup(TargetProduct, Front, "#FFFFFF");
        var config = new GenerateVideoConfig(Mode: "standard", DurationSeconds: 6);

        var result = SceneSourceResolver.Resolve(config, [targetWhite, targetBlack], new Dictionary<Guid, MockupImage>());

        result.Error.Should().BeNull();
        // One scene, not the automatic four: the Generate Video editor shows one scene for a workflow without a list.
        var scene = result.Config.AiScenes.Should().ContainSingle().Subject;
        scene.MockupId.Should().Be(targetBlack.Id);
        scene.DurationSeconds.Should().Be(6);
        scene.Mode.Should().Be("standard");
        result.Scenes.Should().ContainSingle().Which.MockupId.Should().Be(targetBlack.Id);
        result.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public void Resolve_NoSceneListAndSkipPolicy_StillStartsFromTheMainMockup()
    {
        var target = Mockup(TargetProduct, Front, "#FFFFFF");
        var config = new GenerateVideoConfig(Mode: "standard", DurationSeconds: 6, MissingSourcePolicy: "skip");

        var result = SceneSourceResolver.Resolve(config, [target], new Dictionary<Guid, MockupImage>());

        result.Error.Should().BeNull();
        result.Config.AiScenes.Should().ContainSingle().Which.MockupId.Should().Be(target.Id);
    }

    [TestMethod]
    public void Resolve_NoSceneListButSceneOrderOrManualSelection_KeepsTheAutomaticPlan()
    {
        var target = Mockup(TargetProduct, Front, "#FFFFFF");

        var ordered = SceneSourceResolver.Resolve(new GenerateVideoConfig(Mode: "standard", DurationSeconds: 6, SceneOrder: [target.Id]), [target], new Dictionary<Guid, MockupImage>());
        var manual = SceneSourceResolver.Resolve(new GenerateVideoConfig(Mode: "standard", DurationSeconds: 6, AssetSelection: "manual", SelectedMockupIds: [target.Id]), [target], new Dictionary<Guid, MockupImage>());

        ordered.Config.AiScenes.Should().BeNull();
        manual.Config.AiScenes.Should().BeNull();
    }

    [TestMethod]
    public void Resolve_SceneWithoutStartFrame_UsesTheMainMockupWithoutAWarning()
    {
        var target = Mockup(TargetProduct, Front, "#FFFFFF");

        var result = SceneSourceResolver.Resolve(Config(Scene(null, 6)), [target], new Dictionary<Guid, MockupImage>());

        result.Config.AiScenes!.Single().MockupId.Should().Be(target.Id);
        result.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public void Resolve_SceneWithAPerson_StartsFromTheDesignsModelPhoto()
    {
        var previewFront = Mockup(PreviewProduct, Front, "white");
        var targetFront = Mockup(TargetProduct, Front, "white");
        var targetModel = ModelPhoto(TargetProduct);

        var result = SceneSourceResolver.Resolve(Config(PersonScene(previewFront.Id)), [targetFront, targetModel], Referenced(previewFront));

        result.Error.Should().BeNull();
        result.Config.AiScenes![0].MockupId.Should().Be(targetModel.Id);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("model photo");
    }

    [TestMethod]
    public void Resolve_SceneWithAPersonAndNoModelPhoto_CannotRender()
    {
        var previewModel = ModelPhoto(PreviewProduct);
        var targetFront = Mockup(TargetProduct, Front, "white");

        var result = SceneSourceResolver.Resolve(Config(PersonScene(previewModel.Id)), [targetFront], Referenced(previewModel));

        result.Error.Should().Contain("no approved model photo");
    }

    [TestMethod]
    public void Resolve_SceneWithAPersonPickedForTheDesign_KeepsTheSellersImage()
    {
        var targetFront = Mockup(TargetProduct, Front, "white");
        var targetModel = ModelPhoto(TargetProduct);
        var overrides = new TargetOverride([new(0, targetFront.Id)]);

        var result = SceneSourceResolver.Resolve(Config(PersonScene(null)), [targetFront, targetModel],
            new Dictionary<Guid, MockupImage>(), overrides);

        result.Error.Should().BeNull();
        result.Config.AiScenes![0].MockupId.Should().Be(targetFront.Id);
    }

    private static AiVideoSceneOptions PersonScene(Guid? mockupId) =>
        new(MockupId: mockupId, Mode: "ai", TemplateCode: "lifestyle_in_use", DurationSeconds: 5);

    /// <summary>A photo of a model wearing the design, made at Mockup Approval: no template, the Lifestyle role.</summary>
    private static MockupImage ModelPhoto(Guid productId) => new()
    {
        Id = Guid.NewGuid(), ProductId = productId, VariantKey = "white", Role = "Lifestyle",
        ArtworkGroupKey = "design", ApprovalStatus = "approved", MetadataRevision = 1, ApprovedRevision = 1,
        ContentHash = new string('b', 64), Regions = "{}", StorageKey = "key", SourceType = "uploaded"
    };

    private static GenerateVideoConfig Config(params AiVideoSceneOptions[] scenes) =>
        new(Mode: "standard", DurationSeconds: scenes.Sum(scene => scene.DurationSeconds), AiScenes: scenes);

    private static AiVideoSceneOptions Scene(Guid? mockupId, int seconds = 5) =>
        new(MockupId: mockupId, Mode: "standard", DurationSeconds: seconds, StandardMotionPreset: "zoom");

    private static MockupImage Mockup(Guid productId, Guid templateId, string color) => new()
    {
        Id = Guid.NewGuid(), ProductId = productId, MockupTemplateId = templateId, VariantKey = color, Role = "Hero",
        ArtworkGroupKey = "design", ApprovalStatus = "approved", MetadataRevision = 1, ApprovedRevision = 1,
        ContentHash = new string('a', 64), Regions = "{}", StorageKey = "key", SourceType = "generated"
    };

    private static IReadOnlyDictionary<Guid, MockupImage> Referenced(params MockupImage[] mockups) => mockups.ToDictionary(x => x.Id);
}
