using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiVideoPromptCompilerTests
{
    [TestMethod]
    public void Compile_CinematicShowcaseTShirt_UsesTemplateDefaultsWhileAdvancedControlsAreDisabled()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("t_shirt");
        var source = fixture.AddAsset(product);
        source.GarmentColor = "#1F2A44";
        source.BackgroundColor = "#FFFFFF";
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiGlobalInstructions: "Use a warm sunset studio background.",
            AiScenes: [new(MockupId: source.Id, ExtraInstructions: "Keep the print easy to read.")]);

        var storyboard = new VideoStoryboardPlanner().Plan(product, [source], config).Value;
        var prompt = storyboard.Scenes.Single().CompiledPrompt!;

        prompt.Should().Contain("Product context: T-shirt");
        prompt.Should().Contain("hero product view image is the primary reference");
        prompt.Should().Contain("garment color: #1F2A44");
        prompt.Should().Contain("background color: #FFFFFF");
        prompt.Should().Contain("natural fabric movement and soft folds");
        prompt.Should().Contain("background and lighting may develop naturally");
        prompt.Should().NotContain("Shared direction for all scenes: Use a warm sunset studio background.");
        prompt.Should().NotContain("Keep the print easy to read.");
        storyboard.Scenes.Single().PromptVersion.Should().Be(5);
    }

    [TestMethod]
    public void Compile_MacroDetail_AddsDetailContextWithoutLifestyleDirections()
    {
        var template = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "macro_detail_reveal");
        var prompt = AiVideoPromptCompiler.Compile(template, new(TemplateCode: template.Code),
            new("poster", "ArtworkDetail"), 4);

        prompt.Should().Contain("Product context: poster");
        prompt.Should().Contain("artwork or material detail view");
        prompt.Should().Contain("Prioritize the selected close-up or marked detail");
        prompt.Should().Contain("Keep the printed poster surface flat and readable");
        prompt.Should().NotContain("lifestyle scene");
        prompt.Should().NotContain("adult");
    }

    [TestMethod]
    [DataRow("tshirt", "T-shirt", "fabric movement")]
    [DataRow("hoodie", "hoodie", "fabric movement")]
    [DataRow("mug", "mug", "rigid shape")]
    [DataRow("poster", "poster", "poster surface flat")]
    [DataRow("tote_bag", "tote bag", "bag body, straps and artwork")]
    [DataRow("phone_case", "phone case", "camera or button cutouts")]
    public void Compile_ProductType_AddsNormalizedProductSpecificGuidance(string productType, string normalizedName, string guidance)
    {
        var template = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "cinematic_product_showcase");

        var prompt = AiVideoPromptCompiler.Compile(template, new(TemplateCode: template.Code),
            new(productType, "Hero"), 4);

        prompt.Should().Contain($"Product context: {normalizedName}");
        prompt.Should().Contain(guidance);
    }

    [TestMethod]
    public void Compile_MissingColorMetadata_UsesImageColorsAndIgnoresDisabledAdvancedDirections()
    {
        var template = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "cinematic_product_showcase");

        var prompt = AiVideoPromptCompiler.Compile(template,
            new(TemplateCode: template.Code, ExtraInstructions: "Keep the logo visible."),
            new("tshirt", "Hero"), 4, "Use a soft blue backdrop.");

        prompt.Should().Contain("image is the primary reference for the product's visible design, colors and proportions");
        prompt.Should().NotContain("Optional mockup color hints");
        prompt.Should().NotContain("garment color:");
        prompt.Should().NotContain("background color:");
        prompt.Should().NotContain("Shared direction for all scenes: Use a soft blue backdrop.");
        prompt.Should().NotContain("Keep the logo visible.");
        prompt.Should().NotContain("exact opening frame");
    }

    [TestMethod]
    public void Plan_PromptVersionChange_ChangesSceneAndStoryboardFingerprints()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new(MockupId: source.Id)]);
        var planner = new VideoStoryboardPlanner();
        var current = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "cinematic_product_showcase");
        var older = new VideoTemplate
        {
            Code = current.Code,
            EffectsConfig = WorkflowJson.Write(new { aiVideo = current with { PromptVersion = 1 } }),
            IsActive = true
        };

        var currentPlan = planner.Plan(product, [source], config).Value;
        var oldVersionPlan = planner.Plan(product, [source], config, [older]).Value;

        currentPlan.Scenes.Single().PromptVersion.Should().Be(5);
        currentPlan.Scenes.Single().AiModelKey.Should().Be(AiVideoModel.ModelKey);
        currentPlan.Scenes.Single().AiModelName.Should().Be(AiVideoModel.ModelName);
        currentPlan.Scenes.Single().AiResolution.Should().Be(AiVideoModel.Resolution);
        currentPlan.Scenes.Single().AiPricePerSecondUsd.Should().Be(AiVideoModel.PricePerSecondUsd);
        currentPlan.AiShots!.Single().Model.Should().Be(AiVideoModel.ModelName);
        currentPlan.AiShots!.Single().ModelKey.Should().Be(AiVideoModel.ModelKey);
        currentPlan.AiShots!.Single().ProviderKey.Should().Be(AiVideoModel.ProviderKey);
        currentPlan.AiShots!.Single().Resolution.Should().Be(AiVideoModel.Resolution);
        currentPlan.AiShots!.Single().PricePerSecondUsd.Should().Be(AiVideoModel.PricePerSecondUsd);
        oldVersionPlan.Scenes.Single().PromptVersion.Should().Be(1);
        currentPlan.Scenes.Single().CompiledPrompt.Should().Be(oldVersionPlan.Scenes.Single().CompiledPrompt);
        currentPlan.AiShots!.Single().Fingerprint.Should().NotBe(oldVersionPlan.AiShots!.Single().Fingerprint);
        currentPlan.Fingerprint.Should().NotBe(oldVersionPlan.Fingerprint);
    }

    [TestMethod]
    public void Catalog_PersonTemplates_AllowNaturalOcclusionWhileKeepingProductIdentity()
    {
        var templates = AiVideoPromptCompiler.Catalog();
        var lifestyle = templates.Single(item => item.Code == "lifestyle_in_use");
        var ugc = templates.Single(item => item.Code == "creator_pov_ugc");
        var story = templates.Single(item => item.Code == "everyday_story_moment");

        lifestyle.PromptVersion.Should().Be(12);
        lifestyle.CommonPrompt.Should().Contain("brief occlusion");
        lifestyle.BasePrompt.Should().NotContain("remain unobstructed and clearly readable");
        ugc.PromptVersion.Should().Be(3);
        ugc.BasePrompt.Should().Contain("natural hand overlap");
        story.PromptVersion.Should().Be(3);
        story.BasePrompt.Should().Contain("naturally visible");
    }

    [TestMethod]
    public void Compile_NaturalMotion_UsesStepThenHeadTurnAndImmediatePushWithoutInventedDetails()
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var trace = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: "natural_motion"),
            new("tshirt", "Lifestyle", "unverified-red", "unverified-blue"), 4, stableSingleImage: true);

        trace.Parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Key)
            .Should().ContainInOrder("template", "product_context", "template_direction", "product_guardrails");
        trace.FinalPrompt.Should().Contain("Product: T-shirt")
            .And.Contain("any visible design")
            .And.Contain("takes one short, relaxed step toward the camera with the right foot")
            .And.Contain("transfers their weight onto it")
            .And.Contain("briefly glances toward the right-hand side of the frame")
            .And.Contain("head gently follows the gaze")
            .And.Contain("No continuous walking")
            .And.Contain("starting immediately and continuing in a steady, uninterrupted glide")
            .And.Contain("brief occlusion")
            .And.NotContain("Keep the model in the established pose")
            .And.NotContain("No deliberate posing, torso turns or walking")
            .And.NotContain("unverified-red")
            .And.NotContain("unverified-blue")
            .And.NotContain("{{");
    }

    [TestMethod]
    public void Compile_NaturalMotionWithActionPlan_DoesNotAddConflictingDefaultMovement()
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var options = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: "natural_motion",
            PromptRuleVersion: 1, StartingState: "the model stands naturally", PrimaryAction: "turn slowly",
            EndingState: "the model faces the side");
        var rules = AiVideoPromptRules.Apply(template, options);

        var prompt = AiVideoPromptCompiler.CompileTrace(template, options, new("tshirt", "Lifestyle"), 4,
            ruleApplication: rules).FinalPrompt;

        prompt.Should().Contain("Perform one primary action only: turn slowly")
            .And.Contain("following the action plan")
            .And.NotContain("No deliberate posing, torso turns or walking")
            .And.NotContain("takes one short, relaxed step")
            .And.NotContain("head gently follows the gaze")
            .And.NotContain("The garment moves naturally with the body");
    }

    [TestMethod]
    [DataRow("tshirt", "The garment moves naturally with the body", "The bag and any visible straps")]
    [DataRow("hoodie", "The garment moves naturally with the body", "The bag and any visible straps")]
    [DataRow("tote_bag", "The bag and any visible straps move naturally", "forming small folds where the fabric bends")]
    [DataRow("mug", "The product keeps its rigid shape", "The garment moves naturally with the body")]
    [DataRow("poster", "The product keeps its rigid shape", "The garment moves naturally with the body")]
    [DataRow("phone_case", "The product keeps its rigid shape", "The garment moves naturally with the body")]
    public void Compile_NaturalMotion_DescribesMotionAppropriateToProduct(string type, string expected, string excluded)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var prompt = AiVideoPromptCompiler.Compile(template,
            new(TemplateCode: template.Code, SubtemplateCode: "natural_motion"), new(type, "Lifestyle"), 4);

        prompt.Should().Contain(expected).And.NotContain(excluded)
            .And.Contain("takes one short, relaxed step")
            .And.Contain("head gently follows the gaze");
    }

    [TestMethod]
    public void Resolve_VersionSixProductOnModel_UpgradesBothAffectedPromptsAndPreservesKeyframeEdits()
    {
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var oldSubtemplates = current.Subtemplates!.Select(item => item.Code switch
        {
            "natural_motion" => item with { BasePrompt = "Old natural prompt", StartFramePrompt = "User start frame" },
            "profile_tracking" => item with { BasePrompt = "User profile prompt" },
            _ => item
        }).ToArray();
        var old = current with { PromptVersion = 6, BasePrompt = "Old natural prompt", Subtemplates = oldSubtemplates };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var resolved = AiVideoPromptCompiler.Resolve(current.Code, [row])!;

        resolved.PromptVersion.Should().Be(12);
        resolved.Subtemplates!.Single(item => item.Code == "natural_motion").BasePrompt.Should()
            .Be(current.Subtemplates!.Single(item => item.Code == "natural_motion").BasePrompt);
        resolved.Subtemplates!.Single(item => item.Code == "natural_motion").StartFramePrompt.Should().Be("User start frame");
        resolved.Subtemplates!.Single(item => item.Code == "profile_tracking").BasePrompt.Should()
            .Be(current.Subtemplates!.Single(item => item.Code == "profile_tracking").BasePrompt);
        resolved.BasePrompt.Should().Be(current.BasePrompt);
    }

    [TestMethod]
    public void Compile_ProfileTracking_ArcsRightAroundStationaryModelAndPreservesSource()
    {
        var template = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "lifestyle_in_use");
        var profile = template.Subtemplates!.Single(item => item.Code == "profile_tracking");

        var trace = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: profile.Code),
            new("tshirt", "Lifestyle"), 4);
        profile.Defaults.SubjectMotion.Should().Be("minimal");
        profile.Defaults.CameraMovement.Should().Be("orbit_right");
        profile.Defaults.Lighting.Should().Be("source_image");
        template.AllowedSettings["cameraMovement"].Should().Contain("orbit_right");
        template.AllowedSettings["lighting"].Should().Contain("source_image");
        trace.Parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Key)
            .Should().ContainInOrder("template", "product_context", "template_direction", "product_guardrails");
        trace.FinalPrompt.Should().Contain("at a steady, measured pace")
            .And.Contain("right-hand side of the opening image")
            .And.Contain("same spot")
            .And.Contain("keeping the body, head and gaze oriented in the direction established by the opening image")
            .And.Contain("The model does not turn to follow the camera")
            .And.Contain("clearly visible progress throughout the shot")
            .And.Contain("without pausing, speeding up or slowing down")
            .And.Contain("short, shallow arc")
            .And.NotContain("slightly quicker")
            .And.NotContain("eye contact fall away")
            .And.Contain("No walking, stepping or deliberate body turns")
            .And.Contain("Preserve the background and setting shown in the source image")
            .And.Contain("Preserve the lighting shown in the source image")
            .And.Contain("Any visible design remains unchanged")
            .And.NotContain("camera tracks")
            .And.NotContain("courtyard")
            .And.NotContain("parallax")
            .And.NotContain("{{");
        trace.FinalPrompt.Length.Should().BeLessThan(2_500);
    }

    [TestMethod]
    public void Resolve_VersionSevenProfile_UpgradesDefaultsAndPreservesUnrelatedAndCustomFrameDirections()
    {
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var oldSubtemplates = current.Subtemplates!.Select(item => item.Code switch
        {
            "natural_motion" => item with { BasePrompt = "Edited natural prompt" },
            "profile_tracking" => item with { BasePrompt = "Old profile prompt", StartFramePrompt = "My approved-frame direction",
                Defaults = item.Defaults with { CameraMovement = "gentle_tracking", Lighting = "golden_hour" } },
            _ => item
        }).ToArray();
        var oldSettings = current.AllowedSettings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        oldSettings["cameraMovement"] = ["locked", "push_in", "gentle_tracking"];
        oldSettings["lighting"] = ["warm_natural", "golden_hour", "bright_clean"];
        var old = current with { PromptVersion = 7, AllowedSettings = oldSettings, Subtemplates = oldSubtemplates };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var resolved = AiVideoPromptCompiler.Resolve(current.Code, [row])!;
        var profile = resolved.Subtemplates!.Single(item => item.Code == "profile_tracking");

        resolved.PromptVersion.Should().Be(12);
        resolved.Subtemplates!.Single(item => item.Code == "natural_motion").BasePrompt.Should()
            .Be(current.Subtemplates!.Single(item => item.Code == "natural_motion").BasePrompt);
        profile.BasePrompt.Should().Be(current.Subtemplates!.Single(item => item.Code == "profile_tracking").BasePrompt);
        profile.StartFramePrompt.Should().Be("My approved-frame direction");
        profile.Defaults.CameraMovement.Should().Be("orbit_right");
        profile.Defaults.Lighting.Should().Be("source_image");
        resolved.AllowedSettings["cameraMovement"].Should().Contain("orbit_right");
        resolved.AllowedSettings["lighting"].Should().Contain("source_image");
    }

    [TestMethod]
    public void Resolve_VersionEightNaturalMotion_UpgradesNaturalAndPreservesOtherDirections()
    {
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var oldSubtemplates = current.Subtemplates!.Select(item => item.Code switch
        {
            "natural_motion" => item with { BasePrompt = "Previous natural prompt", StartFramePrompt = "My frame" },
            "profile_tracking" => item with { BasePrompt = "My profile prompt", StartFramePrompt = "My profile frame" },
            _ => item
        }).ToArray();
        var old = current with { PromptVersion = 8, BasePrompt = "Previous natural prompt", Subtemplates = oldSubtemplates };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var resolved = AiVideoPromptCompiler.Resolve(current.Code, [row])!;

        resolved.PromptVersion.Should().Be(12);
        resolved.Subtemplates!.Single(item => item.Code == "natural_motion").BasePrompt.Should().Be(current.BasePrompt);
        resolved.Subtemplates!.Single(item => item.Code == "natural_motion").StartFramePrompt.Should().Be("My frame");
        resolved.Subtemplates!.Single(item => item.Code == "profile_tracking").BasePrompt.Should().Be("My profile prompt");
        resolved.Subtemplates!.Single(item => item.Code == "profile_tracking").StartFramePrompt.Should().Be("My profile frame");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Resolve_VersionTenProfile_UpgradesDefaultAndPreservesCustomDirections(bool customProfile)
    {
        const string oldProfilePrompt = "Create one continuous {{DURATION}}-second live-action product-on-model fashion advertisement using the approved source image as the opening frame. {{PRODUCT_CONTEXT}} {{PROFILE_MOTION}} From the first frame, the camera arcs slowly to camera-right around the model, moving toward the right-hand side of the opening image along a short, shallow curve. Keep approximately the same camera height and distance, with the lens aimed at the upper torso. Continue this movement smoothly throughout the shot. Preserve the background and setting shown in the source image. Keep approximately the same framing throughout the camera arc. Any visible design remains unchanged and attached to the same area of the garment. As the camera moves around the model, view the design from the changing camera angle. Do not redraw, stretch or rearrange the design. {{LIGHTING}}";
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var old = current with { PromptVersion = 10, BasePrompt = "My parent prompt",
            Subtemplates = current.Subtemplates!.Select(item => item.Code == "profile_tracking"
                ? item with { BasePrompt = customProfile ? "My profile prompt" : oldProfilePrompt,
                    StartFramePrompt = "My approved frame" }
                : item with { BasePrompt = $"My {item.Code} prompt", StartFramePrompt = $"My {item.Code} frame" }).ToArray() };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var resolved = AiVideoPromptCompiler.Resolve(current.Code, [row])!;
        var profile = resolved.Subtemplates!.Single(item => item.Code == "profile_tracking");

        resolved.PromptVersion.Should().Be(12);
        resolved.BasePrompt.Should().Be(old.BasePrompt);
        profile.BasePrompt.Should().Be(customProfile ? "My profile prompt"
            : current.Subtemplates!.Single(item => item.Code == "profile_tracking").BasePrompt);
        profile.StartFramePrompt.Should().Be("My approved frame");
        profile.Defaults.Should().Be(old.Subtemplates!.Single(item => item.Code == "profile_tracking").Defaults);
        resolved.Subtemplates!.Where(item => item.Code != "profile_tracking").Should()
            .BeEquivalentTo(old.Subtemplates!.Where(item => item.Code != "profile_tracking"));
    }

    [TestMethod]
    public void Catalog_PersistedRightCameraArc_RestoresProfileNameWithoutChangingOtherShots()
    {
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var persisted = current with { Subtemplates = current.Subtemplates!.Select(item => item.Code == "profile_tracking"
            ? item with { Name = "Right Camera Arc", Description = "A broad arc to a rear view." }
            : item).ToArray() };
        var row = new VideoTemplate { Code = current.Code, IsActive = true,
            EffectsConfig = WorkflowJson.Write(new { aiVideo = persisted }) };

        var catalog = AiVideoPromptCompiler.Catalog([row]);
        var resolved = catalog.Single(item => item.Code == current.Code);
        var profile = resolved.Subtemplates!.Single(item => item.Code == "profile_tracking");

        profile.Name.Should().Be("Profile Tracking");
        profile.Description.Should().Contain("slow, smooth, shallow").And.NotContain("rear view");
        profile.BasePrompt.Should().Be(persisted.Subtemplates!.Single(item => item.Code == "profile_tracking").BasePrompt);
        resolved.Subtemplates!.Where(item => item.Code != "profile_tracking").Should()
            .BeEquivalentTo(persisted.Subtemplates!.Where(item => item.Code != "profile_tracking"));
    }

    [TestMethod]
    public void Resolve_VersionSevenProfile_ReplacesOldDefaultStartFrameDirection()
    {
        const string oldFrame = "Create a photorealistic medium eye-level fashion keyframe from a three-quarter side angle. The model stands still with feet planted, torso turned about 45 degrees, neutral head and relaxed arms away from the artwork. Keep enough garment front visible for the design and leave lateral space for an immediate sideways camera track. No walking pose.";
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var old = current with { PromptVersion = 7, Subtemplates = current.Subtemplates!
            .Select(item => item.Code == "profile_tracking" ? item with { StartFramePrompt = oldFrame } : item).ToArray() };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var profile = AiVideoPromptCompiler.Resolve(current.Code, [row])!.Subtemplates!
            .Single(item => item.Code == "profile_tracking");

        profile.StartFramePrompt.Should().Contain("facing mostly forward").And.NotContain("45 degrees");
    }

    [TestMethod]
    public void Compile_ProfileTrackingWithActionPlan_DoesNotContradictTheChosenAction()
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var options = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: "profile_tracking",
            PromptRuleVersion: 1, StartingState: "the model stands", PrimaryAction: "turn slowly",
            EndingState: "the model faces sideways");
        var rules = AiVideoPromptRules.Apply(template, options);

        var prompt = AiVideoPromptCompiler.CompileTrace(template, options, new("tshirt", "Lifestyle"), 4,
            ruleApplication: rules).FinalPrompt;

        prompt.Should().Contain("Perform one primary action only: turn slowly")
            .And.Contain("following the action plan")
            .And.NotContain("No walking, stepping or deliberate body turns")
            .And.NotContain("keeping the body, head and gaze")
            .And.NotContain("The model does not turn to follow the camera");
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public void Plan_ProfileTracking_UsesSupportedRightOrbitAndSourceLighting(string modelKey)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("tshirt");
        var source = fixture.AddAsset(product, "Lifestyle");
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 5,
            AiScenes: [new(MockupId: source.Id, TemplateCode: "lifestyle_in_use",
                SubtemplateCode: "profile_tracking", DurationSeconds: 5, RightsConfirmed: true, AiVideoModelKey: modelKey)]);

        var result = new VideoStoryboardPlanner().Plan(product, [source], config);

        result.IsSuccess.Should().BeTrue();
        var scene = result.Value.Scenes.Single();
        scene.AiSettings!.CameraMovement.Should().Be("orbit_right");
        scene.AiSettings.CameraAngle.Should().Be("source_view");
        scene.AiSettings.Lighting.Should().Be("source_image");
        scene.CompiledPrompt.Should().Contain("with small amplitude at slow speed")
            .And.Contain("30-40 degrees in total across the entire shot")
            .And.Contain("stop at the side-profile boundary")
            .And.NotContain("rear three-quarter");
        scene.CompiledPrompt.Should().Be(scene.AiPromptTrace!.FinalPrompt);
        result.Value.AiShots!.Single().Prompt.Should().Be(scene.CompiledPrompt);
        result.Value.AiShots!.Single().ModelKey.Should().Be(modelKey);
    }

    [TestMethod]
    public void Compile_FabricDetail_FollowsOpeningDetailAndPreservesSourceAngleAndLight()
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var trace = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: "fabric_detail"),
            new("hoodie", "Lifestyle", "unverified-red", "unverified-blue"), 4);

        trace.Parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Key)
            .Should().ContainInOrder("template", "product_context", "template_direction", "product_guardrails");
        trace.FinalPrompt.Should().Contain("Product: hoodie")
            .And.Contain("same visible product detail already emphasized by the opening crop")
            .And.Contain("From the first frame")
            .And.Contain("with small amplitude at slow speed")
            .And.Contain("a restrained cinematic orbit to the right")
            .And.Contain("viewing direction established by the opening image")
            .And.Contain("Preserve the lighting shown in the source image")
            .And.Contain("No deliberate gestures, steps or body turns")
            .And.NotContain("Warm natural light")
            .And.NotContain("three-quarter camera angle")
            .And.NotContain("unverified-red")
            .And.NotContain("unverified-blue")
            .And.NotContain("takes one short, relaxed step")
            .And.NotContain("{{");
    }

    [TestMethod]
    [DataRow("tshirt", "small movement in the visible torso and existing fabric folds", "keeps its rigid shape")]
    [DataRow("hoodie", "small movement in the visible torso and existing fabric folds", "keeps its rigid shape")]
    [DataRow("tote_bag", "any small bag or strap movement", "visible torso and existing fabric folds")]
    [DataRow("mug", "product keeps its rigid shape", "visible torso and existing fabric folds")]
    [DataRow("poster", "product keeps its rigid shape", "visible torso and existing fabric folds")]
    [DataRow("phone_case", "product keeps its rigid shape", "visible torso and existing fabric folds")]
    public void Compile_FabricDetail_UsesProductAppropriateSubjectMotion(string type, string expected, string excluded)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var prompt = AiVideoPromptCompiler.Compile(template,
            new(TemplateCode: template.Code, SubtemplateCode: "fabric_detail"), new(type, "Lifestyle"), 4);

        prompt.Should().Contain(expected).And.NotContain(excluded);
    }

    [TestMethod]
    public void Compile_FabricDetailWithActionPlan_OmitsDefaultMotionRestrictions()
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var options = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: "fabric_detail",
            PromptRuleVersion: 1, StartingState: "a hand rests beside the product", PrimaryAction: "touch the product",
            EndingState: "the hand rests beside the product again");
        var rules = AiVideoPromptRules.Apply(template, options);

        var prompt = AiVideoPromptCompiler.CompileTrace(template, options, new("hoodie", "Lifestyle"), 4,
            ruleApplication: rules).FinalPrompt;

        prompt.Should().Contain("Perform one primary action only: touch the product")
            .And.NotContain("No deliberate gestures, steps or body turns")
            .And.NotContain("Gentle breathing causes small movement");
    }

    [TestMethod]
    public void Resolve_VersionNineFabric_UpgradesDetailDefaultsAndPreservesOtherPromptsAndCustomFrame()
    {
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var old = current with { PromptVersion = 9, Subtemplates = current.Subtemplates!.Select(item => item.Code switch
        {
            "natural_motion" => item with { BasePrompt = "My natural prompt" },
            "profile_tracking" => item with { BasePrompt = "My profile prompt" },
            "fabric_detail" => item with { BasePrompt = "Old fabric prompt", StartFramePrompt = "My frame direction",
                Defaults = item.Defaults with { CameraAngle = "three_quarter", Lighting = "warm_natural" } },
            _ => item
        }).ToArray() };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var resolved = AiVideoPromptCompiler.Resolve(current.Code, [row])!;
        var detail = resolved.Subtemplates!.Single(item => item.Code == "fabric_detail");

        resolved.PromptVersion.Should().Be(12);
        detail.BasePrompt.Should().Be(current.Subtemplates!.Single(item => item.Code == "fabric_detail").BasePrompt);
        detail.Defaults.CameraAngle.Should().Be("source_view");
        detail.Defaults.Lighting.Should().Be("source_image");
        detail.StartFramePrompt.Should().Be("My frame direction");
        resolved.Subtemplates!.Single(item => item.Code == "natural_motion").BasePrompt.Should().Be("My natural prompt");
        resolved.Subtemplates!.Single(item => item.Code == "profile_tracking").BasePrompt.Should().Be("My profile prompt");
    }

    [TestMethod]
    public void Plan_FabricDetail_UsesSupportedSourceAngleAndLighting()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("hoodie");
        var source = fixture.AddAsset(product, "Lifestyle");
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 5,
            AiScenes: [new(MockupId: source.Id, TemplateCode: "lifestyle_in_use", SubtemplateCode: "fabric_detail",
                DurationSeconds: 5, RightsConfirmed: true)]);

        var result = new VideoStoryboardPlanner().Plan(product, [source], config);

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Single().AiSettings!.CameraAngle.Should().Be("source_view");
        result.Value.Scenes.Single().AiSettings!.Lighting.Should().Be("source_image");
        result.Value.Scenes.Single().AiSettings!.CameraMovement.Should().Be("orbit_right");
        result.Value.AiShots!.Single().Prompt.Should().Contain("arcs around the model toward frame-right with small amplitude at slow speed");
    }

    [TestMethod]
    [DataRow(false, "push_in", "orbit_right")]
    [DataRow(false, "locked", "locked")]
    [DataRow(true, "push_in", "push_in")]
    public void Resolve_PersistedFabricPush_UpgradesOnlyKnownBuiltInDirection(bool custom, string oldCamera, string expectedCamera)
    {
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        const string customPrompt = "Approved custom fabric direction: keep the original camera behavior.";
        var old = current with
        {
            AllowedSettings = current.AllowedSettings.ToDictionary(pair => pair.Key,
                pair => pair.Key == "cameraMovement" ? pair.Value.Where(value => value != "orbit_right").ToArray() : pair.Value),
            Subtemplates = current.Subtemplates!.Select(item => item.Code == "fabric_detail"
                ? item with
                {
                    BasePrompt = custom ? customPrompt : AiVideoPromptCompiler.LegacyFabricDetailPushPrompt,
                    Defaults = item.Defaults with { CameraMovement = oldCamera },
                    StartFramePrompt = "My approved frame direction"
                } : item).ToArray()
        };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var resolved = AiVideoPromptCompiler.Resolve(current.Code, [row])!;
        var catalog = AiVideoPromptCompiler.Catalog([row]).Single(item => item.Code == current.Code);
        var detail = resolved.Subtemplates!.Single(item => item.Code == "fabric_detail");

        detail.Defaults.CameraMovement.Should().Be(expectedCamera);
        detail.BasePrompt.Should().Be(custom ? customPrompt : current.Subtemplates!.Single(item => item.Code == "fabric_detail").BasePrompt);
        detail.StartFramePrompt.Should().Be("My approved frame direction");
        catalog.Should().BeEquivalentTo(resolved);
        resolved.Subtemplates!.Where(item => item.Code != "fabric_detail").Should()
            .BeEquivalentTo(old.Subtemplates!.Where(item => item.Code != "fabric_detail"));
        if (expectedCamera == "orbit_right") resolved.AllowedSettings["cameraMovement"].Should().Contain("orbit_right");
    }

    [TestMethod]
    public void Compile_BrandFocus_KeepsVisibleDesignInFrameAndUsesSourceViewAndLighting()
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var trace = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: "brand_focus"),
            new("tshirt", "Lifestyle", "unverified-red", "unverified-blue"), 4);

        trace.Parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Key)
            .Should().ContainInOrder("template", "product_context", "template_direction", "product_guardrails");
        trace.FinalPrompt.Should().StartWith("Create one continuous 4-second live-action brand-focused")
            .And.Contain("Product: T-shirt")
            .And.Contain("If no branding is visible, emphasize the existing silhouette, colors and construction instead")
            .And.Contain("Keep the design area visible in the opening image inside the frame and in focus throughout")
            .And.Contain("Do not invent branding or reveal design areas absent from the source")
            .And.Contain("the model gently lifts their gaze upward, then follows with a small, relaxed lift of the chin")
            .And.Contain("Keep the original hand placement")
            .And.Contain("Settle into the gently raised gaze and continue with natural breathing")
            .And.Contain("From the first frame, the camera performs a slow, smooth cinematic push-in")
            .And.Contain("steady, uninterrupted glide")
            .And.Contain("Keep the approach shallow so the visible design stays within the frame")
            .And.Contain("Maintain the viewing direction established by the opening image")
            .And.Contain("Preserve the background and setting shown in the source image")
            .And.Contain("Preserve the lighting shown in the source image")
            .And.Contain("brief occlusion")
            .And.NotContain("small confident posture adjustment")
            .And.NotContain("golden-hour")
            .And.NotContain("unverified-red")
            .And.NotContain("unverified-blue")
            .And.NotContain("{{");
        trace.FinalPrompt.Length.Should().BeLessThanOrEqualTo(AiVideoPromptCompiler.MaximumPromptCharacters);
    }

    [TestMethod]
    [DataRow("tshirt", "Product: T-shirt", "The garment moves naturally with the body", "The product keeps its rigid shape")]
    [DataRow("hoodie", "Product: hoodie", "The garment moves naturally with the body", "Product: T-shirt")]
    [DataRow("tote_bag", "Product: tote bag", "The bag and any visible straps move naturally", "forming small folds where the fabric bends")]
    [DataRow("mug", "Product: mug", "The product keeps its rigid shape", "forming small folds where the fabric bends")]
    public void Compile_BrandFocus_UsesProductAppropriateMotion(string type, string product, string expected, string excluded)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var prompt = AiVideoPromptCompiler.Compile(template,
            new(TemplateCode: template.Code, SubtemplateCode: "brand_focus"), new(type, "Lifestyle"), 4);

        prompt.Should().Contain(product).And.Contain(expected).And.NotContain(excluded);
    }

    [TestMethod]
    public void Compile_BrandFocusWithActionPlan_DoesNotAddConflictingDefaultMovement()
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var options = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: "brand_focus",
            PromptRuleVersion: 1, StartingState: "the model stands naturally", PrimaryAction: "turn slowly",
            EndingState: "the model faces the side");
        var rules = AiVideoPromptRules.Apply(template, options);

        var prompt = AiVideoPromptCompiler.CompileTrace(template, options, new("tshirt", "Lifestyle"), 4,
            ruleApplication: rules).FinalPrompt;

        prompt.Should().Contain("Perform one primary action only: turn slowly")
            .And.Contain("following the action plan")
            .And.NotContain("the shoulders gently lower and settle")
            .And.NotContain("gently lifts their gaze upward")
            .And.NotContain("No steps, deliberate body turns or gestures across the product")
            .And.NotContain("The garment moves naturally with the body");
    }

    [TestMethod]
    [DataRow(false, null)]
    [DataRow(false, "legacy")]
    [DataRow(false, "My approved frame")]
    [DataRow(true, "My approved frame")]
    public void Resolve_VersionElevenBrand_UpgradesDefaultAndPreservesCustomDirections(bool customPrompt, string? frame)
    {
        const string oldPrompt = "Create one continuous medium product-on-model editorial shot from the approved source image. Use torso framing and keep the garment's visible artwork, lettering, or logo as the visual focus; if the garment has no branding, emphasize its real silhouette, color blocking, and construction instead. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}} for the full {{DURATION}} seconds. The model makes a small confident posture adjustment while keeping the product naturally visible. {{LIGHTING}} Preserve accessories only when they already exist in the source. Use natural fabric folds, warm commercial color, shallow depth of field, and restrained film grain.";
        const string oldFrame = "Create a photorealistic medium-close, eye-level fashion keyframe that is nearly front-facing. Center the garment artwork, logo or silhouette and keep it flat, readable and fully unobstructed. The model stands naturally with hands outside the product area, clean commercial light and a simple coherent background.";
        var current = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var currentBrand = current.Subtemplates!.Single(item => item.Code == "brand_focus");
        var old = current with { PromptVersion = 11, BasePrompt = "My parent prompt",
            Subtemplates = current.Subtemplates!.Select(item => item.Code == "brand_focus"
                ? item with { BasePrompt = customPrompt ? "My brand prompt" : oldPrompt,
                    StartFramePrompt = frame == "legacy" ? oldFrame : frame,
                    Defaults = item.Defaults with { CameraAngle = "eye_level", Lighting = "golden_hour" } }
                : item with { BasePrompt = $"My {item.Code} prompt", StartFramePrompt = $"My {item.Code} frame" }).ToArray() };
        var row = new VideoTemplate { Code = current.Code, IsActive = true, EffectsConfig = WorkflowJson.Write(new { aiVideo = old }) };

        var resolved = AiVideoPromptCompiler.Resolve(current.Code, [row])!;
        var brand = resolved.Subtemplates!.Single(item => item.Code == "brand_focus");

        resolved.PromptVersion.Should().Be(12);
        resolved.BasePrompt.Should().Be(old.BasePrompt);
        brand.BasePrompt.Should().Be(customPrompt ? "My brand prompt" : currentBrand.BasePrompt);
        brand.StartFramePrompt.Should().Be(frame is null or "legacy" ? currentBrand.StartFramePrompt : frame);
        brand.Defaults.CameraAngle.Should().Be(customPrompt ? "eye_level" : "source_view");
        brand.Defaults.Lighting.Should().Be(customPrompt ? "golden_hour" : "source_image");
        resolved.Subtemplates!.Where(item => item.Code != "brand_focus").Should()
            .BeEquivalentTo(old.Subtemplates!.Where(item => item.Code != "brand_focus"));
    }

    [TestMethod]
    public void Plan_BrandFocus_UsesSourceViewLightingAndPushIn()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("hoodie");
        var source = fixture.AddAsset(product, "Lifestyle");
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 5,
            AiScenes: [new(MockupId: source.Id, TemplateCode: "lifestyle_in_use",
                SubtemplateCode: "brand_focus", DurationSeconds: 5, RightsConfirmed: true)]);

        var result = new VideoStoryboardPlanner().Plan(product, [source], config);

        result.IsSuccess.Should().BeTrue();
        var scene = result.Value.Scenes.Single();
        scene.PromptVersion.Should().Be(12);
        scene.AiSettings!.CameraMovement.Should().Be("push_in");
        scene.AiSettings.CameraAngle.Should().Be("source_view");
        scene.AiSettings.Lighting.Should().Be("source_image");
        scene.CompiledPrompt.Should().Contain("The same hoodie remains on the same model")
            .And.Contain("the model gently lifts their gaze upward")
            .And.Contain("camera pushes in with small amplitude at slow speed");
    }

    [TestMethod]
    public void Catalog_ProductOnModelSubtemplates_ExposeShortDistinctStartFrameDirections()
    {
        var productOnModel = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "lifestyle_in_use");

        productOnModel.Subtemplates!.Select(item => item.Code)
            .Should().Equal("natural_motion", "fabric_detail", "brand_focus", "profile_tracking");
        productOnModel.Subtemplates.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.StartFramePrompt) && item.StartFramePrompt!.Length < 500);
        productOnModel.Subtemplates.Single(item => item.Code == "profile_tracking").StartFramePrompt.Should()
            .Contain("facing mostly forward").And.Contain("No walking pose").And.NotContain("45 degrees");
        productOnModel.Subtemplates.Single(item => item.Code == "brand_focus").StartFramePrompt.Should()
            .Contain("existing visible artwork").And.Contain("Preserve the source viewing direction")
            .And.NotContain("keep it flat");
        productOnModel.Subtemplates.Single(item => item.Code == "fabric_detail").StartFramePrompt.Should()
            .Contain("one real product detail").And.Contain("Preserve the source viewing direction");
    }

    [TestMethod]
    public void CompileTrace_VerifiedExperiences_UsesAtMostThreeAndKeepsThemSeparateFromRules()
    {
        var template = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "lifestyle_in_use");
        var experiences = Enumerable.Range(1, 4)
            .Select(number => new VideoPromptExperience(Guid.NewGuid(), number.ToString(), $"Lesson {number}: keep motion restrained."))
            .ToArray();

        var trace = AiVideoPromptCompiler.CompileTrace(template, new(TemplateCode: template.Code),
            new("tshirt", "Lifestyle"), 4, experiences: experiences);

        trace.RagStatus.Should().Be("applied");
        trace.RetrievedExperiences.Should().HaveCount(3);
        trace.Parts.Single(part => part.Key == "rag").Content.Should().Contain("Lesson 1").And.Contain("Lesson 3").And.NotContain("Lesson 4");
        trace.Parts.Single(part => part.Key == "product_guardrails").Content.Should()
            .Contain("Keep the same model and exact product throughout the shot")
            .And.Contain("brief occlusion");
        trace.FinalPrompt.Should().Contain("Verified experience for this scene")
            .And.Contain("Apply it only when it does not conflict with the template")
            .And.Contain("Keep the same model and exact product throughout the shot");
    }

    [TestMethod]
    public void CompileTrace_WhenLessonsWouldExceedMiniMaxLimit_OmitsLowestRelevanceLessonsFirst()
    {
        var template = AiVideoPromptCompiler.Catalog().Single(item => item.Code == "lifestyle_in_use");
        var experiences = Enumerable.Range(1, 3)
            .Select(number => new VideoPromptExperience(Guid.NewGuid(), number.ToString(),
                $"Lesson {number}: " + new string((char)('a' + number - 1), 2_500)))
            .ToArray();

        var trace = AiVideoPromptCompiler.CompileTrace(template, new(TemplateCode: template.Code),
            new("tshirt", "Lifestyle"), 4, experiences: experiences);

        trace.FinalPrompt.Length.Should().BeLessThanOrEqualTo(AiVideoPromptCompiler.MaximumPromptCharacters);
        trace.RetrievedExperiences.Should().HaveCountLessThan(3);
        trace.RetrievedExperiences.Select(item => item.LessonId).Should().BeEquivalentTo(
            experiences.Take(trace.RetrievedExperiences.Count).Select(item => item.LessonId), options => options.WithStrictOrdering());
        trace.Warnings.Should().Contain(warning => warning.Contains("low-relevance RAG lesson", StringComparison.Ordinal));
    }
}
