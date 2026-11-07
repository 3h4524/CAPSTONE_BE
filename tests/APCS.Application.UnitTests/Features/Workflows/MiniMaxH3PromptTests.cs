using APCS.Application.Features.Workflows;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class MiniMaxH3PromptTests
{
    private const string FirstFrame = "For the target video, at 0.00 seconds into the target video, <Picture 1> (from [Shot 1]) is fully referenced.";

    [TestMethod]
    [DataRow("natural_motion", AiVideoModel.ModelKey)]
    [DataRow("fabric_detail", AiVideoModel.ModelKey)]
    [DataRow("brand_focus", AiVideoModel.ModelKey)]
    [DataRow("profile_tracking", AiVideoModel.ModelKey)]
    [DataRow("natural_motion", AiVideoModel.LiteModelKey)]
    [DataRow("fabric_detail", AiVideoModel.LiteModelKey)]
    [DataRow("brand_focus", AiVideoModel.LiteModelKey)]
    [DataRow("profile_tracking", AiVideoModel.LiteModelKey)]
    public void Plan_H3ProductOnModel_SendsFirstFrameAndThreeOrderedSections(string subtemplate, string modelKey)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("tshirt");
        var source = fixture.AddAsset(product, "Lifestyle");
        var result = new VideoStoryboardPlanner().Plan(product, [source], new(Mode: "ai_shot", DurationSeconds: 6,
            AiScenes: [new(MockupId: source.Id, TemplateCode: "lifestyle_in_use", SubtemplateCode: subtemplate,
                DurationSeconds: 6, RightsConfirmed: true, AiVideoModelKey: modelKey)]));

        result.IsSuccess.Should().BeTrue();
        var scene = result.Value.Scenes.Single();
        var trace = scene.AiPromptTrace!;
        var prompt = trace.FinalPrompt;
        prompt.Should().StartWith(FirstFrame + "\n\nintegrated_multimodal_description: [Shot 1] ");
        prompt.Should().EndWith("\n\nnon_diegetic_music: N/A");
        prompt.Split("integrated_multimodal_description:").Should().HaveCount(2);
        prompt.Split("overall_soundscape:").Should().HaveCount(2);
        prompt.Split("non_diegetic_music:").Should().HaveCount(2);
        prompt.Should().NotContain("[Shot 2]").And.NotContain("{{");
        trace.Parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Key)
            .Should().ContainInOrder("h3_i2va_v1", "h3_visual_description", "template", "product_context",
                "template_direction", "product_guardrails", "h3_soundscape", "h3_music");
        prompt.Should().Be(string.Join("\n\n", trace.Parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Content)));
        scene.CompiledPrompt.Should().Be(prompt);
        result.Value.AiShots!.Single().Prompt.Should().Be(prompt);
        result.Value.AiShots!.Single().MockupId.Should().Be(source.Id);
        result.Value.AiShots!.Single().ModelKey.Should().Be(modelKey);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public void Plan_H3NaturalMotion_SnapshotsTheEffectiveSourceControls(string modelKey)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("tshirt");
        var source = fixture.AddAsset(product, "Lifestyle");
        var result = new VideoStoryboardPlanner().Plan(product, [source], new(Mode: "ai_shot", DurationSeconds: 6,
            AiScenes: [new(MockupId: source.Id, TemplateCode: "lifestyle_in_use", DurationSeconds: 6,
                RightsConfirmed: true, AiVideoModelKey: modelKey)]));

        result.IsSuccess.Should().BeTrue();
        var scene = result.Value.Scenes.Single();
        scene.AiSettings!.CameraAngle.Should().Be("source_view");
        scene.AiSettings.Lighting.Should().Be("source_image");
        scene.CompiledPrompt.Should().Contain("Retain the viewing direction established by the opening image")
            .And.Contain("Preserve the lighting shown in the source image");
    }

    [TestMethod]
    [DataRow("mug")]
    [DataRow("poster")]
    [DataRow("phone_case")]
    public void CompileTrace_H3NaturalMotionWithRigidProduct_DoesNotAddFabricMotion(string productType)
    {
        var prompt = AiVideoPromptCompiler.CompileTrace(AiVideoPromptCompiler.Resolve("lifestyle_in_use")!,
            new(TemplateCode: "lifestyle_in_use", SubtemplateCode: "natural_motion"),
            new(productType, "Lifestyle"), 6, modelKey: AiVideoModel.ModelKey).FinalPrompt;

        prompt.Should().Contain("The product keeps its rigid shape and its established position")
            .And.NotContain("The garment moves naturally with the body");
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public void CompileTrace_H3NaturalMotion_PreservesActionOrderAndUsesSourceLightingAndMeasuredCamera(string modelKey)
    {
        var prompt = CompileNatural(modelKey).FinalPrompt;

        prompt.Should().Contain("takes one short, relaxed step toward the camera with the right foot")
            .And.Contain("gently transfers their weight onto it")
            .And.Contain("a hand already resting in a pocket stays there")
            .And.Contain("The garment moves naturally with the body")
            .And.Contain("The head follows with a small, relaxed turn while the torso maintains its direction")
            .And.Contain("Finish comfortably balanced, looking slightly off-camera")
            .And.Contain("a push-in with small amplitude at slow speed")
            .And.Contain("Retain the viewing direction established by the opening image")
            .And.Contain("Preserve the lighting shown in the source image")
            .And.Contain("Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement")
            .And.NotContain("Warm natural light")
            .And.NotContain("Perform one short step followed by this small head turn");
        prompt.IndexOf("transfers their weight", StringComparison.Ordinal).Should()
            .BeLessThan(prompt.IndexOf("the model glances", StringComparison.Ordinal));
        prompt.Should().NotContain("unverified-red").And.NotContain("unverified-blue");
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public void CompileTrace_H3WithP1_UsesOnlyTheRequestedActionAndKeepsDisabledDirectionsOut(string modelKey)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var options = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: "natural_motion",
            PromptRuleVersion: 1, StartingState: "the adult holds the mug by its handle",
            PrimaryAction: "lift the mug slightly", EndingState: "the mug stays in the same hand",
            ExtraInstructions: "Add confetti.");
        var rules = AiVideoPromptRules.Apply(template, options);

        var trace = AiVideoPromptCompiler.CompileTrace(template, rules.Settings, new("mug", "Lifestyle"), 6,
            sharedInstructions: "Change the clothing.", ruleApplication: rules, modelKey: modelKey);

        trace.RuleVersion.Should().Be(1);
        trace.AppliedRules.Should().Contain("p1-start-action-end");
        trace.FinalPrompt.Should().Contain("begin with the adult holds the mug by its handle")
            .And.Contain("Perform one primary action only: lift the mug slightly")
            .And.Contain("Finish with the mug stays in the same hand")
            .And.NotContain("takes one short, relaxed step")
            .And.NotContain("the model glances")
            .And.NotContain("Add confetti")
            .And.NotContain("Change the clothing");
    }

    [TestMethod]
    [DataRow("lifestyle_in_use", "Lifestyle")]
    [DataRow("cinematic_product_showcase", "Hero")]
    public void CompileTrace_H3Audio_IsSeparatedFromProductPreservation(string code, string sourceRole)
    {
        var template = AiVideoPromptCompiler.Resolve(code)!;
        var trace = AiVideoPromptCompiler.CompileTrace(template, new(TemplateCode: code), new("tshirt", sourceRole), 6,
            modelKey: AiVideoModel.ModelKey);

        var visual = trace.FinalPrompt.Split("\n\noverall_soundscape:")[0];
        visual.Should().NotContain("dialogue").And.NotContain("singing").And.NotContain("music");
        trace.Parts.Single(part => part.Key == "h3_soundscape").Content.Should()
            .Be("overall_soundscape: Quiet ambient sound only. No dialogue, speech, singing or diegetic music.");
        trace.Parts.Single(part => part.Key == "h3_music").Content.Should().Be("non_diegetic_music: N/A");
        trace.Parts.Single(part => part.Key == "product_guardrails").Content.Should()
            .Contain("artwork").And.Contain("watermarks");
    }

    [TestMethod]
    [DataRow(VeoVideoModel.ModelKey)]
    public void CompileTrace_OtherModel_RetainsTheUnformattedDraft(string modelKey)
    {
        var draft = CompileNatural(modelKey: null);
        var other = CompileNatural(modelKey);

        other.FinalPrompt.Should().Be(draft.FinalPrompt);
        other.Parts.Should().BeEquivalentTo(draft.Parts, options => options.WithStrictOrdering());
        other.FinalPrompt.Should().NotContain("integrated_multimodal_description:")
            .And.Contain("starting immediately and continuing in a steady, uninterrupted glide")
            .And.Contain("Warm natural light");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Plan_LiteScene_UsesStructuredFormatAndKeepsEconomyRouting(bool overridePremiumAtScene)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product);
        var result = new VideoStoryboardPlanner().Plan(product, [source], new(Mode: "ai_shot", DurationSeconds: 6,
            AiVideoModelKey: overridePremiumAtScene ? AiVideoModel.ModelKey : AiVideoModel.LiteModelKey,
            AiScenes: [new(MockupId: source.Id, DurationSeconds: 6,
                AiVideoModelKey: overridePremiumAtScene ? AiVideoModel.LiteModelKey : null)]));

        result.IsSuccess.Should().BeTrue();
        result.Value.Scenes.Single().AiModelKey.Should().Be(AiVideoModel.LiteModelKey);
        result.Value.Scenes.Single().CompiledPrompt.Should().StartWith(FirstFrame)
            .And.Contain("integrated_multimodal_description: [Shot 1]");
        var shot = result.Value.AiShots!.Single();
        shot.ModelKey.Should().Be(AiVideoModel.LiteModelKey);
        shot.Model.Should().Be(AiVideoModel.LiteModelName);
        shot.PricePerSecondUsd.Should().Be(AiVideoModel.LitePricePerSecondUsd);
        result.Value.Scenes.Single().AiPricePerSecondUsd.Should().Be(AiVideoModel.LitePricePerSecondUsd);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, 4)]
    [DataRow(AiVideoModel.ModelKey, 6)]
    [DataRow(AiVideoModel.LiteModelKey, 4)]
    [DataRow(AiVideoModel.LiteModelKey, 6)]
    public void CompileTrace_ProfileTracking_UsesSlowBoundedArcAndKeepsOpeningPose(string modelKey, int duration)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var trace = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: "profile_tracking"),
            new("tshirt", "Lifestyle", "unverified-red", "unverified-blue"), duration, modelKey: modelKey);
        var prompt = trace.FinalPrompt;

        prompt.Should().StartWith(FirstFrame).And.EndWith("non_diegetic_music: N/A")
            .And.Contain($"One continuous {duration}-second product-on-model shot")
            .And.Contain("same body orientation, head direction and gaze")
            .And.Contain("Gentle breathing causes small movement in the visible torso and existing fabric folds")
            .And.Contain("The hands keep their original placement")
            .And.Contain("does not walk, step or turn to follow the camera")
            .And.Contain("begins at the viewing direction established by the opening image")
            .And.Contain("arcs around the model toward the right-hand side of the opening image with small amplitude at slow speed")
            .And.Contain("30-40 degrees in total across the entire shot, never more than 40 degrees")
            .And.Contain("glides at an unhurried, even pace, then eases softly to a stop")
            .And.Contain("stop at the side-profile boundary without passing behind the shoulder or revealing the back")
            .And.Contain("If the opening image is already close to profile, shorten the arc")
            .And.Contain("Camera height and distance remain approximately constant")
            .And.Contain("front artwork becomes naturally foreshortened")
            .And.Contain("Never copy the front graphic onto the back")
            .And.Contain("natural occlusion as surfaces turn out of view")
            .And.Contain("Preserve the lighting shown in the source image")
            .And.NotContain("takes one short, relaxed step")
            .And.NotContain("the model glances")
            .And.NotContain("push-in").And.NotContain("large amplitude").And.NotContain("at fast speed")
            .And.NotContain("rear three-quarter").And.NotContain("brisk, steady pace")
            .And.NotContain("perspective and brief occlusion")
            .And.NotContain("Keep the product and any visible design as the visual focus")
            .And.NotContain("unverified-red").And.NotContain("unverified-blue")
            .And.NotContain("{{");
        trace.Parts.Single(part => part.Key == AiVideoPromptCompiler.ProfileOrbitGuardrailKey)
            .IncludedInFinalPrompt.Should().BeTrue();
        var profile = template.Subtemplates!.Single(item => item.Code == "profile_tracking");
        profile.Name.Should().Be("Profile Tracking");
        profile.Description.Should().Contain("slow, smooth, shallow");
        trace.FinalPrompt.Should().Be(string.Join("\n\n", trace.Parts
            .Where(part => part.IncludedInFinalPrompt).Select(part => part.Content)));
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public void CompileTrace_ProfileTrackingWithP1_DoesNotInsertStationaryPoseOrNaturalMotion(string modelKey)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var settings = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: "profile_tracking",
            PromptRuleVersion: 1, StartingState: "the person faces forward", PrimaryAction: "turn slowly to the side",
            EndingState: "the person faces sideways");
        var rules = AiVideoPromptRules.Apply(template, settings);

        var trace = AiVideoPromptCompiler.CompileTrace(template, rules.Settings, new("tshirt", "Lifestyle"), 6,
            ruleApplication: rules, modelKey: modelKey);

        trace.FinalPrompt.Should().Contain("Perform one primary action only: turn slowly to the side")
            .And.Contain("Finish with the person faces sideways")
            .And.Contain("arcs around the model toward the right-hand side")
            .And.NotContain("same body orientation, head direction and gaze")
            .And.NotContain("does not walk, step or turn to follow the camera")
            .And.NotContain("takes one short, relaxed step");
        trace.RuleVersion.Should().Be(1);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public void CompileTrace_CustomProfileTracking_PreservesCatalogDirectionAndDefaults(string modelKey)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        template = template with
        {
            Subtemplates = template.Subtemplates!.Select(item => item.Code == "profile_tracking"
                ? item with { BasePrompt = "Approved custom profile: the camera stays still while the person turns. {{PRODUCT_CONTEXT}} {{LIGHTING}}" }
                : item).ToArray()
        };
        var settings = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: "profile_tracking");

        var trace = AiVideoPromptCompiler.CompileTrace(template, settings, new("tshirt", "Lifestyle"), 6, modelKey: modelKey);

        trace.FinalPrompt.Should().StartWith(FirstFrame)
            .And.Contain("Approved custom profile: the camera stays still while the person turns")
            .And.NotContain("arcs around the model").And.NotContain("One continuous 6-second");
        trace.Parts.Should().NotContain(part => part.Key == AiVideoPromptCompiler.ProfileOrbitGuardrailKey);
        AiVideoPromptCompiler.ApplyDefaults(template, settings, modelKey).CameraAngle.Should().Be("three_quarter");
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, "mug")]
    [DataRow(AiVideoModel.LiteModelKey, "phone_case")]
    public void CompileTrace_ProfileTrackingRigidProduct_DoesNotInventGarmentMovement(string modelKey, string productType)
    {
        var trace = AiVideoPromptCompiler.CompileTrace(AiVideoPromptCompiler.Resolve("lifestyle_in_use")!,
            new(TemplateCode: "lifestyle_in_use", SubtemplateCode: "profile_tracking"),
            new(productType, "Lifestyle"), 6, modelKey: modelKey);
        trace.FinalPrompt.Should().Contain("product keeps its rigid shape and established placement")
            .And.NotContain("breathing causes small movement in the visible torso and existing fabric folds");
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public void CompileTrace_H3CustomNaturalMotion_PreservesThePersistedCreativeDirection(string modelKey)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        template = template with
        {
            Subtemplates = template.Subtemplates!.Select(item => item.Code == "natural_motion"
                ? item with { BasePrompt = "My approved custom direction: the model slowly raises one hand. {{PRODUCT_CONTEXT}} {{LIGHTING}}" }
                : item).ToArray()
        };

        var prompt = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: "natural_motion"), new("tshirt", "Lifestyle"), 6,
            modelKey: modelKey).FinalPrompt;

        prompt.Should().StartWith(FirstFrame)
            .And.Contain("My approved custom direction: the model slowly raises one hand")
            .And.Contain("Warm natural light")
            .And.NotContain("takes one short, relaxed step");
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, "brand_focus")]
    [DataRow(AiVideoModel.LiteModelKey, "brand_focus")]
    [DataRow(AiVideoModel.ModelKey, "fabric_detail")]
    [DataRow(AiVideoModel.LiteModelKey, "fabric_detail")]
    [DataRow(AiVideoModel.ModelKey, "profile_tracking")]
    [DataRow(AiVideoModel.LiteModelKey, "profile_tracking")]
    public void CompileTrace_H3FormatOverhead_TrimsOptionalExperienceBeforeExceedingTheLimit(string modelKey, string subtemplate)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var settings = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: subtemplate);
        var context = new AiVideoProductContext("tshirt", "Lifestyle");
        var neutral = AiVideoPromptCompiler.CompileTrace(template, settings, context, 6, experiences: []);
        var oneCharacter = new VideoPromptExperience(Guid.NewGuid(), "v1", "x");
        var withOneCharacter = AiVideoPromptCompiler.CompileTrace(template, settings, context, 6, experiences: [oneCharacter]);
        var lessonLength = AiVideoPromptCompiler.MaximumPromptCharacters - neutral.FinalPrompt.Length
            - (withOneCharacter.FinalPrompt.Length - neutral.FinalPrompt.Length - 1);
        var lesson = oneCharacter with { Content = new string('x', lessonLength) };
        var oldFormat = AiVideoPromptCompiler.CompileTrace(template, settings, context, 6, experiences: [lesson]);

        oldFormat.FinalPrompt.Length.Should().Be(AiVideoPromptCompiler.MaximumPromptCharacters);
        oldFormat.RetrievedExperiences.Should().ContainSingle();

        var formatted = AiVideoPromptCompiler.CompileTrace(template, settings, context, 6, experiences: [lesson],
            modelKey: modelKey);

        formatted.FinalPrompt.Length.Should().BeLessThanOrEqualTo(AiVideoPromptCompiler.MaximumPromptCharacters);
        formatted.RetrievedExperiences.Should().BeEmpty();
        formatted.Warnings.Should().ContainSingle().Which.Should().Contain("low-relevance RAG lesson");
        formatted.FinalPrompt.Should().StartWith(FirstFrame).And.EndWith("non_diegetic_music: N/A")
            .And.Contain("Keep the same model and exact product throughout the shot");
        if (subtemplate == "profile_tracking")
            formatted.FinalPrompt.Should().Contain("Never copy the front graphic onto the back");
        formatted.FinalPrompt.Should().Be(string.Join("\n\n", formatted.Parts
            .Where(part => part.IncludedInFinalPrompt).Select(part => part.Content)));
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, 4)]
    [DataRow(AiVideoModel.LiteModelKey, 6)]
    [DataRow(AiVideoModel.ModelKey, 8)]
    [DataRow(AiVideoModel.LiteModelKey, 4)]
    public void CompileTrace_FabricDetail_DevelopsSameOpeningDetailWithSlowArc(string modelKey, int duration)
    {
        var trace = AiVideoPromptCompiler.CompileTrace(AiVideoPromptCompiler.Resolve("lifestyle_in_use")!,
            new(TemplateCode: "lifestyle_in_use", SubtemplateCode: "fabric_detail"), new("tshirt", "Lifestyle"),
            duration, modelKey: modelKey);

        trace.FinalPrompt.Should().StartWith(FirstFrame)
            .And.Contain($"One continuous {duration}-second product-detail shot")
            .And.Contain("The opening crop establishes one visible product detail")
            .And.Contain("camera arcs around the model toward frame-right with small amplitude at slow speed")
            .And.Contain("ease softly to a stop at a slightly more side-on view of the same detail")
            .And.Contain("same camera height, distance, focal length and detail scale")
            .And.Contain("Restrained background parallax and natural surface foreshortening")
            .And.Contain("Shorten the arc if the opening crop is already tight")
            .And.Contain("Stop before passing behind the shoulder or revealing the back")
            .And.Contain("do not reconstruct parts of the artwork cropped out of the opening image")
            .And.Contain("Gentle breathing causes small movement in the visible torso and existing fabric folds")
            .And.Contain("Keep the original hand placement")
            .And.Contain("Preserve the lighting shown in the source image")
            .And.NotContain("Product:").And.NotContain("takes one short").And.NotContain("shoulders gently lower")
            .And.NotContain("camera pushes in").And.NotContain("slightly larger").And.NotContain("30-40 degrees")
            .And.EndWith("non_diegetic_music: N/A");
        trace.Parts.Single(part => part.Key == AiVideoPromptCompiler.FabricDetailGuardrailKey)
            .Content.Should().Contain("Do not switch to another detail")
            .And.Contain("No push-in, zoom, pull-back, full orbit or focus pull");
        trace.FinalPrompt.Length.Should().BeLessThanOrEqualTo(AiVideoPromptCompiler.MaximumPromptCharacters);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, 6)]
    [DataRow(AiVideoModel.LiteModelKey, 4)]
    [DataRow(AiVideoModel.ModelKey, 8)]
    public void CompileTrace_BrandFocus_CombinesNaturalUpwardGazeWithSlowPushIn(string modelKey, int duration)
    {
        var trace = AiVideoPromptCompiler.CompileTrace(AiVideoPromptCompiler.Resolve("lifestyle_in_use")!,
            new(TemplateCode: "lifestyle_in_use", SubtemplateCode: "brand_focus"), new("hoodie", "Lifestyle"),
            duration, modelKey: modelKey);

        trace.FinalPrompt.Should().StartWith(FirstFrame)
            .And.Contain($"One continuous {duration}-second brand-focused")
            .And.Contain("The same hoodie remains on the same model")
            .And.Contain("If no branding is visible, the existing silhouette, colors and construction")
            .And.Contain("camera pushes in with small amplitude at slow speed")
            .And.Contain("gently lifts their gaze upward, then follows with a small, relaxed lift of the chin")
            .And.Contain("Settle into the gently raised gaze and continue with natural breathing")
            .And.Contain("If the opening gaze is already raised, use only a slight comfortable adjustment")
            .And.Contain("Keep the head and face in frame when visible in the opening image")
            .And.Contain("Do not crop tighter onto the logo alone")
            .And.Contain("At the same time, from the opening frame the camera pushes in")
            .And.Contain("Finish with the model and existing design or unbranded product sharing the composition")
            .And.Contain("Preserve the lighting shown in the source image")
            .And.NotContain("Product:").And.NotContain("takes one short").And.NotContain("model glances")
            .And.EndWith("non_diegetic_music: N/A");
        trace.Parts.Single(part => part.Key == AiVideoPromptCompiler.BrandFocusGuardrailKey)
            .Content.Should().Contain("Do not invent branding, complete hidden design areas")
            .And.Contain("gently lifts their gaze upward")
            .And.Contain("a small, relaxed lift of the chin");
    }

    [TestMethod]
    [DataRow("fabric_detail", AiVideoModel.ModelKey)]
    [DataRow("fabric_detail", AiVideoModel.LiteModelKey)]
    [DataRow("brand_focus", AiVideoModel.ModelKey)]
    [DataRow("brand_focus", AiVideoModel.LiteModelKey)]
    public void CompileTrace_DetailOrBrandWithP1_PreservesExplicitActionWithoutDefaultBodyMotion(string subtemplate, string modelKey)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        var settings = new AiVideoSceneOptions(TemplateCode: template.Code, SubtemplateCode: subtemplate,
            PromptRuleVersion: 1, StartingState: "the hand rests beside the product",
            PrimaryAction: "raise the free hand once", EndingState: "the free hand remains raised");
        var rules = AiVideoPromptRules.Apply(template, settings);

        var trace = AiVideoPromptCompiler.CompileTrace(template, rules.Settings, new("tshirt", "Lifestyle"), 6,
            ruleApplication: rules, modelKey: modelKey);

        trace.FinalPrompt.Should().Contain("Perform one primary action only: raise the free hand once")
            .And.Contain("Finish with the free hand remains raised")
            .And.NotContain("Keep the original hand placement").And.NotContain("No deliberate gestures")
            .And.NotContain("shoulders gently lower").And.NotContain("No steps, deliberate body turns")
            .And.NotContain("gently lifts their gaze upward").And.NotContain("a small, relaxed lift of the chin");
        trace.FinalPrompt.Should().Contain(subtemplate == "fabric_detail"
            ? "camera arcs around the model toward frame-right with small amplitude at slow speed"
            : "camera pushes in with small amplitude at slow speed");
    }

    [TestMethod]
    [DataRow("fabric_detail", AiVideoModel.ModelKey)]
    [DataRow("fabric_detail", AiVideoModel.LiteModelKey)]
    [DataRow("brand_focus", AiVideoModel.ModelKey)]
    [DataRow("brand_focus", AiVideoModel.LiteModelKey)]
    public void CompileTrace_CustomDetailOrBrand_PreservesCatalogDirection(string subtemplate, string modelKey)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        template = template with { Subtemplates = template.Subtemplates!.Select(item => item.Code == subtemplate
            ? item with { BasePrompt = "Approved custom direction: hold the camera still. {{PRODUCT_CONTEXT}} {{LIGHTING}}" }
            : item).ToArray() };

        var trace = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: subtemplate), new("mug", "Lifestyle"), 6, modelKey: modelKey);

        trace.FinalPrompt.Should().Contain("Approved custom direction: hold the camera still")
            .And.NotContain("camera pushes in");
        trace.Parts.Should().NotContain(part => part.Key == AiVideoPromptCompiler.FabricDetailGuardrailKey ||
            part.Key == AiVideoPromptCompiler.BrandFocusGuardrailKey);
    }

    [TestMethod]
    [DataRow("fabric_detail", AiVideoModel.ModelKey, "mug")]
    [DataRow("fabric_detail", AiVideoModel.LiteModelKey, "phone_case")]
    [DataRow("brand_focus", AiVideoModel.ModelKey, "poster")]
    [DataRow("brand_focus", AiVideoModel.LiteModelKey, "mug")]
    public void CompileTrace_DetailOrBrandRigidProduct_DoesNotInventFabricMotion(string subtemplate, string modelKey, string productType)
    {
        var trace = AiVideoPromptCompiler.CompileTrace(AiVideoPromptCompiler.Resolve("lifestyle_in_use")!,
            new(TemplateCode: "lifestyle_in_use", SubtemplateCode: subtemplate), new(productType, "Lifestyle"),
            6, modelKey: modelKey);

        trace.FinalPrompt.Should().Contain("product keeps its rigid shape")
            .And.NotContain("existing fabric folds").And.NotContain("forming small folds where the fabric bends");
    }

    [TestMethod]
    [DataRow("fabric_detail", AiVideoModel.ModelKey)]
    [DataRow("brand_focus", AiVideoModel.LiteModelKey)]
    public void CompileTrace_DetailOrBrandWithCustomCameraDefault_PreservesSelectedCamera(string subtemplate, string modelKey)
    {
        var template = AiVideoPromptCompiler.Resolve("lifestyle_in_use")!;
        template = template with { Subtemplates = template.Subtemplates!.Select(item => item.Code == subtemplate
            ? item with { Defaults = item.Defaults with { CameraMovement = "locked" } } : item).ToArray() };

        var trace = AiVideoPromptCompiler.CompileTrace(template,
            new(TemplateCode: template.Code, SubtemplateCode: subtemplate), new("tshirt", "Lifestyle"), 6, modelKey: modelKey);

        trace.FinalPrompt.Should().Contain("a locked-off camera").And.NotContain("camera pushes in");
        trace.Parts.Should().NotContain(part => part.Key == AiVideoPromptCompiler.FabricDetailGuardrailKey ||
            part.Key == AiVideoPromptCompiler.BrandFocusGuardrailKey);
    }

    private static AiPromptTrace CompileNatural(string? modelKey = AiVideoModel.ModelKey) =>
        AiVideoPromptCompiler.CompileTrace(AiVideoPromptCompiler.Resolve("lifestyle_in_use")!,
            new(TemplateCode: "lifestyle_in_use", SubtemplateCode: "natural_motion"),
            new("tshirt", "Lifestyle", "unverified-red", "unverified-blue"), 6, modelKey: modelKey);
}
