using APCS.Application.Features.Workflows;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoStoryboardPlannerTests
{
    private readonly VideoStoryboardPlanner _planner = new();

    [TestMethod]
    [DataRow("tshirt", .97)]
    [DataRow("hoodie", .97)]
    [DataRow("mug", .985)]
    [DataRow("poster", .97)]
    [DataRow("tote_bag", .97)]
    [DataRow("phone_case", .985)]
    public void Plan_SixProductProfiles_ProducesSafeContinuousStandardScenes(string productType, double scale)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct(productType);
        var asset = fixture.AddAsset(product);
        var result = _planner.Plan(product, [asset], new(TextOverlay: "Original artwork"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Should().HaveCount(2);
        result.Value.Scenes.Sum(x => x.DurationFrames).Should().Be(360);
        result.Value.Scenes.Select(x => x.SceneOrder).Should().Equal(0, 1);
        foreach (var scene in result.Value.Scenes)
        {
            scene.GenerationStrategy.Should().Be("standard");
            scene.SourceRevision.Should().Be(3);
            scene.SourceHash.Should().Be(asset.ContentHash);
            scene.Crop.Should().Be(new Region(0, 0, 1, 1));
            scene.EndCrop.Width.Should().BeApproximately(scale, 1e-9);
            scene.EndCrop.X.Should().BeApproximately((1 - scale) / 2, 1e-9);
            scene.Text.Should().Be("Original artwork");
            scene.Warnings.Should().BeEmpty();
        }
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(13)]
    [DataRow(15)]
    public void Plan_FourScenes_DistributesAllFramesWithoutChangingDuration(int duration)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var assets = new[] { fixture.AddAsset(product, "Lifestyle"), fixture.AddAsset(product, "ArtworkDetail"), fixture.AddAsset(product, "Hero"), fixture.AddAsset(product, "AlternativeAngle") };
        var result = _planner.Plan(product, assets, new(Template: "product_showcase", DurationSeconds: duration));
        result.Value.Scenes.Select(x => x.Role).Should().Equal("Hero", "AlternativeAngle", "Lifestyle", "ArtworkDetail");
        result.Value.Scenes.Sum(x => x.DurationFrames).Should().Be(duration * 30);
        result.Value.Scenes.Max(x => x.DurationFrames).Should().BeLessThanOrEqualTo(result.Value.Scenes.Min(x => x.DurationFrames) + 1);
    }

    [TestMethod]
    public void Plan_AutoTemplate_PrefersVariantsThenArtworkDetail()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var detail = fixture.AddAsset(product, "ArtworkDetail");
        _planner.Plan(product, [hero, detail], new()).Value.Template.Should().Be("design_detail");
        hero.VariantKey = "red";
        detail.VariantKey = "blue";
        _planner.Plan(product, [hero, detail], new()).Value.Template.Should().Be("variant_showcase");
    }

    [TestMethod]
    public void Plan_VersionTwoSingleUnmarkedAsset_CreatesTwoSafeContainScenes()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        asset.Regions = "{}";

        var result = _planner.Plan(product, [asset], new(TemplateVersion: 2));

        result.Value.TemplateVersion.Should().Be(2);
        result.Value.Scenes.Should().HaveCount(2).And.OnlyContain(scene =>
            scene.MockupId == asset.Id &&
            scene.Crop == new Region(0, 0, 1, 1) && scene.EndCrop == new Region(0, 0, 1, 1));
        result.Value.Scenes.Select(scene => scene.Motion).Should().Equal("contain_gentle", "static");
    }

    [TestMethod]
    public void Plan_VersionTwoDesignDetail_PutsMarkedDetailBeforeUnmarkedHero()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var markedDetail = fixture.AddAsset(product);
        markedDetail.Regions = WorkflowJson.Write(new MockupRegions(Detail: new(.25, .25, .5, .5)));

        var result = _planner.Plan(product, [hero, markedDetail], new(TemplateVersion: 2));

        result.Value.Template.Should().Be("design_detail");
        result.Value.Scenes[0].MockupId.Should().Be(markedDetail.Id);
    }

    [TestMethod]
    public void Plan_VersionTwoVariantTemplate_PrioritizesDistinctVariantsDeterministically()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var redHero = fixture.AddAsset(product); redHero.VariantKey = "red";
        var redAngle = fixture.AddAsset(product, "AlternativeAngle"); redAngle.VariantKey = "red";
        var blue = fixture.AddAsset(product, "Variant"); blue.VariantKey = "blue";

        var result = _planner.Plan(product, [redAngle, blue, redHero], new(TemplateVersion: 2));

        result.Value.Template.Should().Be("variant_showcase");
        result.Value.Scenes.Take(2).Select(x => x.MockupId).Should().Equal(redHero.Id, blue.Id);
    }

    [TestMethod]
    public void Plan_VersionTwoExplicitTemplateWithoutRequirements_ReturnsValidation()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);

        _planner.Plan(product, [asset], new(Template: "design_detail", TemplateVersion: 2)).Error.Message.Should().Contain("requires");
        _planner.Plan(product, [asset], new(Template: "variant_showcase", TemplateVersion: 2)).Error.Message.Should().Contain("two named variants");
    }

    [TestMethod]
    public void CreateScene_VersionTwoUnsafePan_FallsBackWithoutClippingProtectedProduct()
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        asset.Regions = WorkflowJson.Write(new MockupRegions(Product: new(.02, .2, .96, .6)));

        var scene = _planner.CreateScene(asset, "tshirt", new(StandardOptions: new("pan_left"), TemplateVersion: 2), 0, 180);

        scene.Motion.Should().Be("contain_gentle");
        scene.Crop.Should().Be(new Region(0, 0, 1, 1));
        scene.EndCrop.Should().Be(scene.Crop);
        scene.Warnings.Should().Contain("Motion reduced to preserve product/artwork bounds.");
    }

    [TestMethod]
    public void Plan_ManualSelectionAndOrder_ExcludesOtherAssetsAndHonorsExactOrder()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var angle = fixture.AddAsset(product, "AlternativeAngle");
        var omitted = fixture.AddAsset(product, "Lifestyle");
        var result = _planner.Plan(product, [hero, angle, omitted], new(AssetSelection: "manual", SelectedMockupIds: [hero.Id, angle.Id], SceneOrder: [angle.Id, hero.Id]));
        result.Value.Scenes.Select(x => x.MockupId).Should().Equal(angle.Id, hero.Id);
        result.Value.Scenes.Should().NotContain(x => x.MockupId == omitted.Id);
    }

    [TestMethod]
    public void Plan_UnapprovedDeletedStaleAndForeignAssets_WaitsForValidInput()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var unapproved = fixture.AddAsset(product); unapproved.ApprovalStatus = "pending";
        var deleted = fixture.AddAsset(product); deleted.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        var stale = fixture.AddAsset(product); stale.MetadataRevision++;
        var foreign = WorkflowTestData.Asset(Guid.NewGuid());
        var result = _planner.Plan(product, [unapproved, deleted, stale, foreign], new());
        result.Error.Code.Should().Be("WaitingForInput");
    }

    [TestMethod]
    public void Plan_DifferentArtworkGroups_RejectsMixedDesignVideo()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var other = fixture.AddAsset(product); other.ArtworkGroupKey = "other";
        _planner.Plan(product, [hero, other], new()).Error.Message.Should().Contain("share an artwork group");
    }

    [TestMethod]
    public void Plan_UnavailableSceneOrder_RejectsWithoutSubstitutingAsset()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        _planner.Plan(product, [asset], new(SceneOrder: [Guid.NewGuid()])).Error.Message.Should().Contain("unavailable mockup");
    }

    [TestMethod]
    public void Plan_Fingerprint_IsDeterministicAndChangesWithRevisionOrConfiguration()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var initial = _planner.Plan(product, [asset], new()).Value.Fingerprint;
        _planner.Plan(product, [asset], new()).Value.Fingerprint.Should().Be(initial);
        _planner.Plan(product, [asset], new(TextOverlay: "Changed")).Value.Fingerprint.Should().NotBe(initial);
        asset.MetadataRevision++; asset.ApprovedRevision++;
        _planner.Plan(product, [asset], new()).Value.Fingerprint.Should().NotBe(initial);
    }

    [TestMethod]
    public void CreateScene_ProtectedProductOutsideVerticalCrop_UsesStaticContainWithWarning()
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        asset.MockupWidthPx = 2160; asset.MockupHeightPx = 2160;
        asset.Regions = WorkflowJson.Write(new MockupRegions(Product: new(0, .2, 1, .6)));
        var scene = _planner.CreateScene(asset, "tshirt", new(), 0, 180);
        scene.Motion.Should().Be("static");
        scene.Crop.Should().Be(new Region(0, 0, 1, 1));
        scene.EndCrop.Should().Be(scene.Crop);
        scene.Warnings.Should().Contain(x => x.Contains("clip the protected region"));
    }

    [TestMethod]
    public void CreateScene_MissingRegionsAndLowResolution_PreservesImageAndReportsBothWarnings()
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        asset.MockupWidthPx = 500; asset.MockupHeightPx = 900; asset.Regions = "{}";
        var scene = _planner.CreateScene(asset, "mug", new(), 0, 180);
        scene.Motion.Should().Be("static");
        scene.Crop.Should().Be(new Region(0, 0, 1, 1));
        scene.Warnings.Should().HaveCount(2).And.Contain(x => x.Contains("not marked")).And.Contain(x => x.Contains("Low-resolution"));
    }

    [TestMethod]
    public void CreateScene_ZoomWouldClipProtectedRegion_ReducesMotionToStatic()
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        asset.Regions = WorkflowJson.Write(new MockupRegions(Product: new(0, 0, 1, 1)));
        var scene = _planner.CreateScene(asset, "poster", new(), 0, 180);
        scene.Motion.Should().Be("static");
        scene.EndCrop.Should().Be(scene.Crop);
        scene.Warnings.Should().Contain("Motion reduced to preserve product/artwork bounds.");
    }

    [TestMethod]
    public void CreateScene_StaticPreset_LeavesCameraPathUnchanged()
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        asset.MockupWidthPx = 2160; asset.MockupHeightPx = 2160;
        asset.Regions = WorkflowJson.Write(new MockupRegions(Product: new(.4, .2, .2, .6)));
        var scene = _planner.CreateScene(asset, "poster", new(StandardOptions: new("static")), 0, 180);
        scene.Motion.Should().Be("static");
        scene.EndCrop.Should().Be(scene.Crop);
    }

    [TestMethod]
    [DataRow("zoom", true)]
    [DataRow("zoom_out", false)]
    public void CreateScene_ZoomPresets_ChangeScaleInRequestedDirection(string preset, bool zoomIn)
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        var scene = _planner.CreateScene(asset, "tshirt", new(StandardOptions: new(preset)), 0, 180);

        scene.Motion.Should().Be(preset);
        scene.Crop.Width.Should().BeApproximately(zoomIn ? 1 : .92, 1e-9);
        scene.EndCrop.Width.Should().BeApproximately(zoomIn ? .92 : 1, 1e-9);
        scene.Crop.X.Should().BeApproximately(zoomIn ? 0 : .04, 1e-9);
        scene.EndCrop.X.Should().BeApproximately(zoomIn ? .04 : 0, 1e-9);
        scene.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("pan", 1, 0)]
    [DataRow("pan_left", -1, 0)]
    [DataRow("pan_up", 0, -1)]
    [DataRow("pan_down", 0, 1)]
    [DataRow("diagonal_up_right", 1, -1)]
    [DataRow("diagonal_up_left", -1, -1)]
    [DataRow("diagonal_down_right", 1, 1)]
    [DataRow("diagonal_down_left", -1, 1)]
    public void CreateScene_PanPresets_MoveCameraInRequestedDirection(string preset, int horizontal, int vertical)
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        var scene = _planner.CreateScene(asset, "tshirt", new(StandardOptions: new(preset)), 0, 180);

        scene.Motion.Should().Be(preset);
        scene.Crop.Width.Should().BeApproximately(.9, 1e-9);
        scene.EndCrop.Width.Should().BeApproximately(.9, 1e-9);
        Math.Sign(scene.EndCrop.X - scene.Crop.X).Should().Be(horizontal);
        Math.Sign(scene.EndCrop.Y - scene.Crop.Y).Should().Be(vertical);
        scene.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public void CreateScene_PanWouldClipProtectedProduct_UsesStaticContain()
    {
        var asset = WorkflowTestData.Asset(Guid.NewGuid());
        asset.Regions = WorkflowJson.Write(new MockupRegions(Product: new(.02, .2, .96, .6)));

        var scene = _planner.CreateScene(asset, "tshirt", new(StandardOptions: new("pan_left")), 0, 180);

        scene.Motion.Should().Be("static");
        scene.Crop.Should().Be(new Region(0, 0, 1, 1));
        scene.EndCrop.Should().Be(scene.Crop);
        scene.Warnings.Should().Contain("Motion reduced to preserve product/artwork bounds.");
    }

    [TestMethod]
    public void Plan_VariedMotion_UsesDifferentPresetsAcrossFourScenes()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var assets = new[] { fixture.AddAsset(product, "Hero"), fixture.AddAsset(product, "AlternativeAngle"),
            fixture.AddAsset(product, "Lifestyle"), fixture.AddAsset(product, "ArtworkDetail") };

        var result = _planner.Plan(product, assets, new(StandardOptions: new("varied"), SceneOrder: assets.Select(x => x.Id).ToArray()));

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Select(x => x.Motion).Should().Equal("zoom", "pan_left", "diagonal_up_right", "zoom_out");
        result.Value.Scenes.Should().OnlyContain(x => x.Crop != x.EndCrop && x.Warnings.Count == 0);
    }

    [TestMethod]
    public void Plan_PerSceneMotion_OverridesOnlySelectedPositions()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var assets = new[] { fixture.AddAsset(product, "Hero"), fixture.AddAsset(product, "AlternativeAngle"),
            fixture.AddAsset(product, "Lifestyle") };

        var result = _planner.Plan(product, assets, new(StandardOptions: new("varied"), SceneOrder: assets.Select(x => x.Id).ToArray(),
            SceneMotionPresets: ["pan_down", null, "zoom_out"]));

        result.Value.Scenes.Select(x => x.Motion).Should().Equal("pan_down", "pan_left", "zoom_out");
        result.Value.Scenes.Select(x => x.MockupId).Should().Equal(assets.Select(x => x.Id));
    }

    [TestMethod]
    public void Plan_SingleAiShot_ReplacesOnlyChosenSceneAndPreservesTotalFrames()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var angle = fixture.AddAsset(product, "AlternativeAngle");
        var lifestyle = fixture.AddAsset(product, "Lifestyle");
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 13,
            SceneOrder: [hero.Id, angle.Id, lifestyle.Id], TextOverlay: "Keep standard text",
            AiShotOptions: new(SceneIndex: 1, MockupId: angle.Id));

        var result = _planner.Plan(product, [hero, angle, lifestyle], config);

        result.IsSuccess.Should().BeTrue();
        var board = result.Value;
        board.Mode.Should().Be("ai_shot");
        board.Scenes.Select(x => x.GenerationStrategy).Should().Equal("standard", "ai_shot", "standard");
        board.Scenes.Select(x => x.DurationFrames).Should().Equal(105, 180, 105);
        board.Scenes[1].MockupId.Should().Be(angle.Id);
        board.Scenes[1].Text.Should().Be("Keep standard text");
        board.Scenes[0].Text.Should().Be("Keep standard text");
        board.AiShots.Should().ContainSingle().Which.Should().Match<AiShotPlan>(x =>
            x.SceneOrder == 1 && x.MockupId == angle.Id && x.DurationSeconds == 6 && x.Model == "veo-3.1-lite-generate-preview");
    }

    [TestMethod]
    [DataRow(8, 1, 0)]
    [DataRow(15, 2, 30)]
    public void Plan_FullAiVideo_UsesEightSecondClipsAndExpectedOverlap(int duration, int clipCount, int overlap)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var angle = fixture.AddAsset(product, "AlternativeAngle");
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: duration,
            AiShotOptions: new(Coverage: "full_video", FullSceneMockupIds: [hero.Id, angle.Id]));

        var result = _planner.Plan(product, [hero, angle], config);

        result.IsSuccess.Should().BeTrue();
        result.Value.Mode.Should().Be("ai_shot");
        result.Value.Scenes.Should().HaveCount(clipCount).And.OnlyContain(x =>
            x.GenerationStrategy == "ai_shot" && x.DurationFrames == 240 && x.Motion == "static");
        result.Value.AiShots.Should().HaveCount(clipCount).And.OnlyContain(x => x.DurationSeconds == 8);
        result.Value.TransitionOverlapFrames.Should().Be(overlap);
        (result.Value.Scenes.Sum(x => x.DurationFrames) - overlap).Should().Be(duration * 30);
        if (clipCount == 2) result.Value.Scenes.Select(x => x.MockupId).Should().Equal(hero.Id, angle.Id);
    }

    [TestMethod]
    public void Plan_AiShotSourceRevisionOrPromptChange_ChangesClipAndStoryboardFingerprints()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiShotOptions: new(MockupId: asset.Id));
        var original = _planner.Plan(product, [asset], config).Value;
        var prompted = _planner.Plan(product, [asset], config with { AiShotOptions = config.AiShotOptions! with { Prompt = "Slow tracking" } }).Value;
        asset.ContentHash = new string('b', 64);
        asset.MetadataRevision++;
        asset.ApprovedRevision++;
        var changedSource = _planner.Plan(product, [asset], config).Value;

        prompted.Fingerprint.Should().NotBe(original.Fingerprint);
        prompted.AiShots![0].Fingerprint.Should().NotBe(original.AiShots![0].Fingerprint);
        changedSource.Fingerprint.Should().NotBe(original.Fingerprint);
        changedSource.AiShots![0].Fingerprint.Should().NotBe(original.AiShots![0].Fingerprint);
    }

    [TestMethod]
    public void Plan_MaterialDetailWithoutDetailSource_RejectsAiShot()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var result = _planner.Plan(product, [hero], new(Mode: "ai_shot",
            AiShotOptions: new(Concept: "material_detail", MockupId: hero.Id)));
        result.Error.Message.Should().Contain("Material Detail");
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void Plan_LifestyleConceptWithoutRightsOrLifestyleSource_Rejects(bool lifestyleSource, bool rights)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product, lifestyleSource ? "Lifestyle" : "Hero");
        var result = _planner.Plan(product, [source], new(Mode: "ai_shot",
            AiShotOptions: new(Concept: "lifestyle_motion", MockupId: source.Id, RightsConfirmed: rights)));
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Lifestyle");
    }

    [TestMethod]
    public void Plan_FullLifestyleShotWithNonLifestyleSecondSource_RejectsWholeStoryboard()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var lifestyle = fixture.AddAsset(product, "Lifestyle");
        var hero = fixture.AddAsset(product);
        var result = _planner.Plan(product, [lifestyle, hero], new(Mode: "ai_shot", DurationSeconds: 15,
            AiShotOptions: new(Coverage: "full_video", Concept: "product_in_use", MockupId: lifestyle.Id,
                RightsConfirmed: true, FullSceneMockupIds: [lifestyle.Id, hero.Id])));
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Every lifestyle AI scene");
    }

    [TestMethod]
    public void Plan_ApprovedStudioFrame_IsUsedForEveryShowcaseSceneWithSafeMotion()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var frame = CreateStudioFrame(source);
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 8,
            AiScenes: [new(), new(CameraMovement: "pan_right")], AiStudioMode: "studio",
            AiStudioLayout: AiStudioFrameIdentity.HangingWindow, AiStudioSourceMockupId: source.Id, AiStudioFrameId: frame.Id);

        var result = _planner.Plan(product, [source, frame], config);

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Should().HaveCount(2).And.OnlyContain(scene => scene.MockupId == frame.Id &&
            scene.SourceHash == frame.ContentHash && scene.CompiledPrompt!.Contains("keep the set, background and light direction consistent"));
        result.Value.Scenes[0].CompiledPrompt.Should().Contain("no fast zoom, orbit");
        result.Value.Scenes[0].CompiledPrompt.Should().Contain("no floating garment");
        result.Value.Scenes[0].CompiledPrompt.Should().EndWith("This constraint takes precedence over any earlier direction suggesting a changing background, lighting setup, viewpoint, or product support.");
        result.Value.AiShots!.Select(shot => shot.Fingerprint).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void Plan_StudioCameraOrFrameChanges_ChangesSceneFingerprint()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var frame = CreateStudioFrame(source);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new()], AiStudioMode: "studio",
            AiStudioSourceMockupId: source.Id, AiStudioFrameId: frame.Id);

        var original = _planner.Plan(product, [source, frame], config).Value;
        var changedCamera = _planner.Plan(product, [source, frame], config with
        {
            AiScenes = [new(CameraMovement: "pan_left")]
        }).Value;
        frame.ContentHash = new string('c', 64);
        frame.MetadataRevision++;
        frame.ApprovedRevision++;
        var changedFrame = _planner.Plan(product, [source, frame], config).Value;

        changedCamera.AiShots!.Single().Fingerprint.Should().NotBe(original.AiShots!.Single().Fingerprint);
        changedFrame.AiShots!.Single().Fingerprint.Should().NotBe(original.AiShots!.Single().Fingerprint);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("pending")]
    [DataRow("rejected")]
    [DataRow("stale_source")]
    [DataRow("wrong_layout")]
    [DataRow("different_product")]
    public void Plan_StudioMode_InvalidFrameStates_RejectsFrame(string state)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var frame = CreateStudioFrame(source, state == "wrong_layout" ? AiStudioFrameIdentity.FlatLay : AiStudioFrameIdentity.HangingWindow);
        var frameId = frame.Id;
        switch (state)
        {
            case "missing": frameId = Guid.Empty; break;
            case "pending": frame.ApprovalStatus = "pending"; frame.ApprovedRevision = null; break;
            case "rejected": frame.ApprovalStatus = "rejected"; frame.ApprovedRevision = null; break;
            case "stale_source": source.MetadataRevision++; source.ApprovedRevision++; break;
            case "different_product": frame.ProductId = Guid.NewGuid(); break;
        }
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new()], AiStudioMode: "studio",
            AiStudioLayout: AiStudioFrameIdentity.HangingWindow, AiStudioSourceMockupId: source.Id,
            AiStudioFrameId: frameId == Guid.Empty ? null : frameId);

        var result = _planner.Plan(product, [source, frame], config);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(state == "missing" ? "StudioFrameRequired" : "StudioFrameStale");
    }

    [TestMethod]
    public void Plan_StudioMode_NonShowcaseScene_KeepsOriginalSource()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var frame = CreateStudioFrame(source);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new(TemplateCode: "signature_product_reveal")],
            AiStudioMode: "studio", AiStudioSourceMockupId: source.Id, AiStudioFrameId: frame.Id);

        var result = _planner.Plan(product, [source, frame], config);

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Single().MockupId.Should().Be(source.Id);
        result.Value.Scenes.Single().CompiledPrompt.Should().NotContain("keep the set, background and light direction consistent");
    }

    [TestMethod]
    public void Plan_DirectMode_UsesApprovedMockupAsStartFrame()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);

        var result = _planner.Plan(product, [source], new(Mode: "ai_shot", AiScenes: [new()]));

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Single().MockupId.Should().Be(source.Id);
        result.Value.Scenes.Single().CompiledPrompt.Should().NotContain("reviewed studio start frame");
    }

    private static MockupImage CreateStudioFrame(MockupImage source, string layout = AiStudioFrameIdentity.HangingWindow)
    {
        var frame = WorkflowTestData.Asset(source.ProductId, "VideoScene");
        frame.SourceType = MockupSources.AiGenerated;
        frame.MetadataRevision = 1;
        frame.ApprovedRevision = 1;
        frame.ContentHash = new string('b', 64);
        var aspectRatio = VideoOutputFormats.Resolve(VideoOutputFormats.Default).AspectRatio;
        var metadata = new AiStudioFrameMetadata(AiStudioFrameIdentity.Fingerprint(source, layout, aspectRatio), source.Id,
            source.ContentHash!, source.MetadataRevision, layout, aspectRatio, AiStudioFrameIdentity.PromptVersion);
        frame.Regions = AiStudioFrameIdentity.SerializeRegions(metadata);
        return frame;
    }
}
