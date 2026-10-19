using APCS.Application.Features.Workflows;
using APCS.Application.Features.Workflows.Validators;
using System.Text.Json;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowValidatorTests
{
    [TestMethod]
    [DataRow("undefined_config")]
    [DataRow("nonfinite_position")]
    public void SaveWorkflow_UnserializableNode_ReturnsValidationInsteadOfThrowing(string invalid)
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        definition = definition with { Nodes = definition.Nodes.Select(x => x.Type == "generate-video"
            ? invalid == "undefined_config" ? x with { Config = default } : x with { Position = new(double.NaN, 0) }
            : x).ToArray() };
        var result = new SaveWorkflowValidator().Validate(new SaveWorkflowRequest("Product video", "", definition));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [TestMethod]
    [DataRow("api_key")]
    [DataRow("API-Secret")]
    [DataRow("secret")]
    [DataRow("token")]
    [DataRow("Access_Token")]
    [DataRow("password")]
    [DataRow("provider")]
    [DataRow("Provider-Name")]
    [DataRow("connection_string")]
    [DataRow("Signed_URL")]
    public void SaveWorkflow_NestedCredentialOrProviderKey_RejectsPersistingSensitiveConfiguration(string key)
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        var config = JsonSerializer.SerializeToElement(new { nested = new object[] { new { safe = "value" }, new Dictionary<string, object> { [key] = "synthetic-not-a-real-secret" } } });
        definition = definition with { Nodes = definition.Nodes.Select(x => x.Type == "generate-video" ? x with { Config = config } : x).ToArray() };
        var result = new SaveWorkflowValidator().Validate(new SaveWorkflowRequest("Safe workflow name", "", definition));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == "Definition.Nodes" && x.ErrorMessage.Contains("credentials or provider routing"));
    }

    [TestMethod]
    public void SaveWorkflow_PromptMentionsCredentialWordsButContainsNoSensitiveKeys_RemainsValid()
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        var config = JsonSerializer.SerializeToElement(new { textOverlay = "The provider never needs an API key in this video", nested = new object[] { new { mode = "standard", label = "secret garden" } } });
        definition = definition with { Nodes = definition.Nodes.Select(x => x.Type == "generate-video" ? x with { Config = config } : x).ToArray() };
        new SaveWorkflowValidator().Validate(new SaveWorkflowRequest("Product video", "", definition)).IsValid.Should().BeTrue();
    }
    [TestMethod]
    [DataRow(3, true)]
    [DataRow(12, true)]
    [DataRow(30, true)]
    [DataRow(2, false)]
    [DataRow(31, false)]
    public void VideoConfig_DurationBoundary_AcceptsOnlyThreeToThirty(int duration, bool valid)
    {
        var result = new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(DurationSeconds: duration));
        result.IsValid.Should().Be(valid);
        if (!valid) result.Errors.Should().Contain(x => x.PropertyName == "DurationSeconds");
    }

    [TestMethod]
    [DataRow("standard")]
    [DataRow("ai_shot")]
    public void VideoConfig_SupportedMode_RemainsRepresentableWithoutSelectingProvider(string mode)
    {
        new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(Mode: mode,
            AiShotOptions: mode == "ai_shot" ? new() : null)).IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void VideoConfig_LegacyAiBackground_IsRejected()
    {
        new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(Mode: "ai_background"))
            .Errors.Should().Contain(x => x.PropertyName == "Mode");
    }

    [TestMethod]
    public void VideoConfig_Standard_RejectsVariantShowcase()
    {
        var validator = new GenerateVideoConfigValidator();

        validator.Validate(new GenerateVideoConfig(Template: "product_showcase")).IsValid.Should().BeTrue();
        validator.Validate(new GenerateVideoConfig(Template: "design_detail")).IsValid.Should().BeTrue();
        validator.Validate(new GenerateVideoConfig(Template: "variant_showcase"))
            .Errors.Should().Contain(x => x.ErrorMessage.Contains("Product Showcase and Design Detail"));
    }

    [TestMethod]
    [DataRow("gentle")]
    [DataRow("static")]
    [DataRow("zoom")]
    [DataRow("zoom_out")]
    [DataRow("pan")]
    [DataRow("pan_left")]
    [DataRow("pan_up")]
    [DataRow("pan_down")]
    [DataRow("diagonal_up_right")]
    [DataRow("diagonal_up_left")]
    [DataRow("diagonal_down_right")]
    [DataRow("diagonal_down_left")]
    public void VideoConfig_MotionPreset_AcceptsLegacyAndDirectionalValues(string preset)
    {
        new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(StandardOptions: new(preset)))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("fade")]
    [DataRow("swipe")]
    [DataRow("zoom")]
    [DataRow("flash")]
    [DataRow("whip_pan")]
    [DataRow("blur")]
    [DataRow("cut")]
    public void VideoConfig_Transition_AcceptsCurrentAndLegacyValues(string transition)
    {
        new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(StandardOptions: new(Transition: transition)))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void VideoConfig_PerSceneMotion_AcceptsOverridesAndRejectsInvalidOrExcessEntries()
    {
        var validator = new GenerateVideoConfigValidator();

        validator.Validate(new GenerateVideoConfig(StandardOptions: new("varied"), SceneMotionPresets: ["zoom", null, "pan_left"]))
            .IsValid.Should().BeTrue();
        validator.Validate(new GenerateVideoConfig(SceneMotionPresets: ["spin"]))
            .Errors.Should().Contain(x => x.PropertyName == "SceneMotionPresets");
        validator.Validate(new GenerateVideoConfig(SceneMotionPresets: ["zoom", "pan", "static", "zoom_out", "pan_up"]))
            .Errors.Should().Contain(x => x.PropertyName == "SceneMotionPresets");
    }

    [TestMethod]
    public void VideoConfig_StandardSceneFraming_AcceptsNormalizedEndpointsAndRejectsInvalidBoundsOrSources()
    {
        var validator = new GenerateVideoConfigValidator();
        var id = Guid.NewGuid();
        var fullImage = new Region(0, 0, 1, 1);
        var detailCrop = new Region(.1, .1, .8, .8);

        validator.Validate(new GenerateVideoConfig(StandardScenes: [new(id, fullImage, detailCrop)]))
            .IsValid.Should().BeTrue();

        foreach (var invalid in new[]
        {
            new StandardSceneOptions(id, new(-.01, .1, .5, .5), detailCrop),
            new StandardSceneOptions(id, new(.8, .1, .3, .5), detailCrop),
            new StandardSceneOptions(id, new(.1, .1, 0, .5), detailCrop),
            new StandardSceneOptions(id, detailCrop, new(.1, .8, .5, .3)),
            new StandardSceneOptions(id, detailCrop, detailCrop, new(.8, .1, .3, .5)),
            new StandardSceneOptions(Guid.Empty, detailCrop, detailCrop),
        })
        {
            validator.Validate(new GenerateVideoConfig(StandardScenes: [invalid]))
                .Errors.Should().Contain(x => x.PropertyName == "StandardScenes");
        }

        validator.Validate(new GenerateVideoConfig(StandardScenes: [new(id, detailCrop, detailCrop), new(id, detailCrop, detailCrop)]))
            .Errors.Should().Contain(x => x.PropertyName == "StandardScenes");
        var five = Enumerable.Range(0, 5).Select(_ => new StandardSceneOptions(Guid.NewGuid(), detailCrop, detailCrop)).ToArray();
        validator.Validate(new GenerateVideoConfig(StandardScenes: five))
            .Errors.Should().Contain(x => x.PropertyName == "StandardScenes");
    }

    [TestMethod]
    public void VideoConfig_InvalidDiscriminators_ReportsAllUnsupportedFields()
    {
        var result = new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(Mode: "openmontage", Target: "tiktok", Template: "slideshow", AssetSelection: "all"));
        result.Errors.Select(x => x.PropertyName).Should().Contain(["Mode", "Target", "Template", "AssetSelection"]);
    }

    [TestMethod]
    public void VideoConfig_StandardWithDormantAiShotOptions_RemainsValid()
    {
        var config = new GenerateVideoConfig(AiShotOptions: new());
        var result = new GenerateVideoConfigValidator().Validate(config);
        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void VideoConfig_ManualSelection_RequiresDistinctAndAtMostEightMockups()
    {
        var validator = new GenerateVideoConfigValidator();
        validator.Validate(new GenerateVideoConfig(AssetSelection: "manual", SelectedMockupIds: [])).IsValid.Should().BeFalse();
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        validator.Validate(new GenerateVideoConfig(AssetSelection: "manual", SelectedMockupIds: ids)).IsValid.Should().BeTrue();
        validator.Validate(new GenerateVideoConfig(SelectedMockupIds: [.. ids, Guid.NewGuid()])).Errors.Should().Contain(x => x.PropertyName == "SelectedMockupIds");
        validator.Validate(new GenerateVideoConfig(SelectedMockupIds: [ids[0], ids[0]])).Errors.Should().Contain(x => x.PropertyName == "SelectedMockupIds");
    }

    [TestMethod]
    public void VideoConfig_UnsafeCropAndSceneOrder_RejectsConfiguration()
    {
        var id = Guid.NewGuid();
        var result = new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(SceneOrder: [id, id], StandardOptions: new("gentle", "fill", "spin"), TextOverlay: new string('x', 121)));
        result.Errors.Select(x => x.PropertyName).Should().Contain(["SceneOrder", "StandardOptions", "TextOverlay"]);
    }

    [TestMethod]
    [DataRow("single_scene", 12, true)]
    [DataRow("single_scene", 15, true)]
    [DataRow("single_scene", 8, false)]
    [DataRow("full_video", 8, true)]
    [DataRow("full_video", 15, true)]
    [DataRow("full_video", 12, false)]
    public void VideoConfig_AiShotCoverageDuration_EnforcesSupportedCombinations(string coverage, int duration, bool valid)
    {
        new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: duration,
            AiShotOptions: new(Coverage: coverage)))
            .IsValid.Should().Be(valid);
    }

    [TestMethod]
    [DataRow("coverage")]
    [DataRow("concept")]
    [DataRow("scene")]
    [DataRow("prompt")]
    [DataRow("source_ids")]
    public void VideoConfig_AiShotInvalidOption_RejectsSpecificPartition(string invalid)
    {
        var options = invalid switch
        {
            "coverage" => new AiShotOptions(Coverage: "unknown"),
            "concept" => new AiShotOptions(Concept: "unknown"),
            "scene" => new AiShotOptions(SceneIndex: 4),
            "prompt" => new AiShotOptions(Prompt: new string('x', 501)),
            _ => new AiShotOptions(FullSceneMockupIds: [Guid.Empty])
        };
        new GenerateVideoConfigValidator().Validate(new GenerateVideoConfig(Mode: "ai_shot", AiShotOptions: options))
            .Errors.Should().Contain(x => x.PropertyName == "AiShotOptions");
    }

    [TestMethod]
    public void VideoConfig_AiShotWithoutOptionsOrLifestyleRights_RejectsConfiguration()
    {
        var validator = new GenerateVideoConfigValidator();
        validator.Validate(new GenerateVideoConfig(Mode: "ai_shot"))
            .Errors.Should().Contain(x => x.PropertyName == "AiShotOptions");
        validator.Validate(new GenerateVideoConfig(Mode: "ai_shot", AiShotOptions: new(Concept: "lifestyle_motion")))
            .Errors.Should().Contain(x => x.ErrorMessage.Contains("rights"));
    }

    [TestMethod]
    [DataRow("Hero")]
    [DataRow("ArtworkDetail")]
    [DataRow("AlternativeAngle")]
    [DataRow("Lifestyle")]
    [DataRow("Variant")]
    public void Metadata_SupportedRoleAndBoundaryRegions_AcceptsNormalizedGeometry(string role)
    {
        new MockupMetadataValidator().Validate(new MockupMetadataRequest(1, role, "group", null, new(new(0, 1), new(0, 0, 1, 1))))
            .IsValid.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(-.01, .5, .2, .2)]
    [DataRow(.9, .5, .2, .2)]
    [DataRow(.2, .2, 0, .2)]
    [DataRow(.2, .9, .2, .2)]
    [DataRow(double.NaN, .2, .2, .2)]
    public void Metadata_InvalidRegions_RejectsUnsafeGeometry(double x, double y, double width, double height)
    {
        var result = new MockupMetadataValidator().Validate(new MockupMetadataRequest(1, "Hero", "group", null, new(Product: new(x, y, width, height))));
        result.Errors.Should().Contain(e => e.PropertyName == "Regions");
    }

    [TestMethod]
    public void Metadata_InvalidRevisionRoleAndFocalPoint_ReportsIndependentFailures()
    {
        var result = new MockupMetadataValidator().Validate(new MockupMetadataRequest(0, "unknown", "", null, new(new(double.PositiveInfinity, .5))));
        result.Errors.Select(x => x.PropertyName).Should().Contain(["ExpectedRevision", "Role", "ArtworkGroupKey", "Regions"]);
    }

    [TestMethod]
    public void SaveWorkflow_LegacySchemaAndOversizedName_RejectsRequest()
    {
        var product = new WorkflowTestData().AddProduct();
        var result = new SaveWorkflowValidator().Validate(new SaveWorkflowRequest(new string('x', 121), "", WorkflowTestData.Definition(product) with { Version = 1 }));
        result.Errors.Select(x => x.PropertyName).Should().Contain(["Name", "Definition.Version"]);
    }
}
