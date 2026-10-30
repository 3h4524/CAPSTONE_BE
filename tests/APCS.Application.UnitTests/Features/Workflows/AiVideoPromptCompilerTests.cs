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

        lifestyle.PromptVersion.Should().Be(3);
        lifestyle.BasePrompt.Should().Contain("brief natural occlusion");
        lifestyle.BasePrompt.Should().NotContain("remain unobstructed and clearly readable");
        ugc.PromptVersion.Should().Be(3);
        ugc.BasePrompt.Should().Contain("natural hand overlap");
        story.PromptVersion.Should().Be(3);
        story.BasePrompt.Should().Contain("naturally visible");
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
        trace.Parts.Single(part => part.Key == "product_guardrails").Content.Should().Be(AiVideoPromptCompiler.CommonGuardrail);
        trace.FinalPrompt.Should().Contain("Verified experience for this scene").And.Contain(AiVideoPromptCompiler.CommonGuardrail);
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
