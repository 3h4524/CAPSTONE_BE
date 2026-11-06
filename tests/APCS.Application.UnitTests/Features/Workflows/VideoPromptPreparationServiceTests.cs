using System.Security.Cryptography;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoPromptPreparationServiceTests
{
    private const string Opening = "Live-action, a medium view frames the model wearing a printed T-shirt against a plain background.";
    private const string Continuation = "The model takes one short step with the right foot, transfers their weight, then turns their head slightly right. The camera pushes in slowly over a small distance and the model finishes comfortably balanced.";

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public async Task PrepareAsync_ValidObservationAndRewrite_AlignsPromptTraceShotAndFingerprints(string modelKey)
    {
        var fixture = new PreparationFixture(modelKey: modelKey);
        var original = fixture.Board;

        var result = await fixture.PrepareAsync();

        var scene = result.Scenes.Single();
        var shot = result.AiShots!.Single();
        var trace = scene.AiPromptTrace!;
        shot.Prompt.Should().Be(scene.CompiledPrompt).And.Be(trace.FinalPrompt);
        shot.Prompt.Should().Contain("integrated_multimodal_description: [Shot 1] " + Opening + " " + Continuation)
            .And.NotContain("Product: T-shirt").And.NotContain("Create one continuous");
        foreach (var guardrail in original.Scenes.Single().AiPromptTrace!.Parts.Where(part =>
                     part.IncludedInFinalPrompt && part.Key is "product_guardrails" or "source_guardrails" or "template_guardrails"))
            shot.Prompt.Should().Contain(guardrail.Content);
        trace.Parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Content)
            .Should().Equal(shot.Prompt.Split("\n\n"));
        trace.Parts.Should().Contain(part => part.Key == "template" && !part.IncludedInFinalPrompt);
        trace.Preparation!.Status.Should().Be("prepared");
        trace.Preparation.AnalysisModel.Should().Be("vision-model");
        trace.Preparation.RewriteModel.Should().Be("editor-model");
        trace.Preparation.OpeningDescription.Should().Be(Opening);
        trace.Preparation.PreparedFrameHash.Should().Be(Convert.ToHexString(SHA256.HashData(fixture.Pixels)).ToLowerInvariant());
        trace.Preparation.Uncertainties.Should().Equal("Fine lettering is unreadable.");
        shot.Fingerprint.Should().NotBe(original.AiShots!.Single().Fingerprint);
        result.Fingerprint.Should().NotBe(original.Fingerprint);
        shot.MockupId.Should().Be(original.AiShots!.Single().MockupId);
        shot.ModelKey.Should().Be(modelKey);
        shot.Model.Should().Be(original.AiShots!.Single().Model);
        shot.PricePerSecondUsd.Should().Be(modelKey == AiVideoModel.LiteModelKey
            ? AiVideoModel.LitePricePerSecondUsd : AiVideoModel.PricePerSecondUsd);
        fixture.Requests[0].Images!.Single().Bytes.Should().Equal(fixture.Pixels);
        fixture.Requests[1].Images.Should().BeNull();
        fixture.Requests[1].Prompt.Should().Contain(Opening).And.Contain("right foot");
        fixture.Requests.Should().OnlyContain(request => request.Temperature == 0.1m);
    }

    [TestMethod]
    public async Task PrepareAsync_SameInput_UsesOwnerScopedCacheAndKeepsFingerprintsStable()
    {
        var fixture = new PreparationFixture();
        var first = await fixture.PrepareAsync();
        var second = await fixture.PrepareAsync();

        second.Fingerprint.Should().Be(first.Fingerprint);
        second.AiShots.Should().BeEquivalentTo(first.AiShots);
        fixture.Requests.Should().HaveCount(2);
        fixture.Frames.Verify(frame => frame.PrepareAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Region?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.CacheValues.Keys.Should().ContainSingle(key => key.StartsWith($"video-prompt:{WorkflowTestData.Owner:N}:"));

        await fixture.Service.PrepareAsync(Guid.Parse("20000000-0000-0000-0000-000000000001"), fixture.Board, [fixture.Source], default);
        fixture.Requests.Should().HaveCount(4);
        fixture.CacheValues.Should().HaveCount(2);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public async Task PrepareAsync_ProfileTracking_SendsArcBriefWithoutNaturalMotionAndRetainsGrounding(string modelKey)
    {
        const string arcContinuation = "The person keeps the opening pose and gaze while the camera arcs toward frame-right with small amplitude at slow speed. Its total travel is 30-40 degrees over the whole shot, shortened if needed to stop at the side-profile boundary. A stabilized glide eases gently to a stop while the front artwork is naturally foreshortened.";
        var fixture = new PreparationFixture(
            rewrite: JsonSerializer.Serialize(new { continuationDescription = arcContinuation }),
            modelKey: modelKey, subtemplateCode: "profile_tracking");

        var board = await fixture.PrepareAsync();

        fixture.Requests.Should().HaveCount(2);
        fixture.Requests[0].Images!.Single().Bytes.Should().Equal(fixture.Pixels);
        fixture.Requests[1].Prompt.Should().Contain("same body orientation, head direction and gaze")
            .And.Contain("arcs around the model toward the right-hand side of the opening image with small amplitude at slow speed")
            .And.Contain("30-40 degrees in total across the entire shot")
            .And.NotContain("rear three-quarter")
            .And.NotContain("takes one short, relaxed step").And.NotContain("the model glances");
        fixture.Requests[1].SystemInstruction.Should().Contain("with small amplitude at slow speed")
            .And.Contain("angular travel across the entire shot, never degrees per second")
            .And.Contain("Stop at the side-profile boundary");
        var trace = board.Scenes.Single().AiPromptTrace!;
        trace.Preparation!.Status.Should().Be("prepared");
        trace.FinalPrompt.Should().Contain(Opening + " " + arcContinuation)
            .And.Contain("Keep the same model and exact product throughout the shot");
        trace.FinalPrompt.Should().Contain("Never copy the front graphic onto the back");
        trace.FinalPrompt.Should().Contain("Do not travel behind the model or reveal the back");
        trace.Parts.Single(part => part.Key == "retained_" + AiVideoPromptCompiler.ProfileOrbitGuardrailKey)
            .IncludedInFinalPrompt.Should().BeTrue();
        board.AiShots!.Single().Prompt.Should().Be(trace.FinalPrompt);
        board.AiShots!.Single().ModelKey.Should().Be(modelKey);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, "The camera only makes a slight sideways movement while the model stays still.")]
    [DataRow(AiVideoModel.LiteModelKey, "The camera arcs with small amplitude at fast speed over 30-40 degrees, then stops.")]
    [DataRow(AiVideoModel.ModelKey, "The camera arcs with small amplitude at slow speed over 30-40 degrees and reaches a rear three-quarter view.")]
    [DataRow(AiVideoModel.LiteModelKey, "The camera arcs with small amplitude at slow speed over 30-40 degrees per second.")]
    [DataRow(AiVideoModel.ModelKey, "The camera arcs with small amplitude at slow speed over 30-40 degrees, then continues with large amplitude.")]
    public async Task PrepareAsync_ProfileRewriteViolatesSlowArc_RetainsBoundedCompilerPrompt(string modelKey, string continuation)
    {
        var fixture = new PreparationFixture(
            rewrite: JsonSerializer.Serialize(new { continuationDescription = continuation }),
            modelKey: modelKey, subtemplateCode: "profile_tracking");

        var result = await fixture.PrepareAsync();

        AssertFallback(fixture.Board, result);
        result.AiShots!.Single().Prompt.Should().Contain("with small amplitude at slow speed")
            .And.Contain("30-40 degrees in total across the entire shot")
            .And.Contain("stop at the side-profile boundary")
            .And.NotContain("rear three-quarter")
            .And.Contain("Never copy the front graphic onto the back");
        result.AiShots!.Single().ModelKey.Should().Be(modelKey);
    }

    [TestMethod]
    public async Task PrepareAsync_ChangesFromPremiumToEconomy_IsolatesCacheWithoutPromotingLite()
    {
        var fixture = new PreparationFixture();
        var premium = await fixture.PrepareAsync();
        var product = new Product { Id = fixture.Source.ProductId, ProductType = "tshirt" };
        var liteDraft = new VideoStoryboardPlanner().Plan(product, [fixture.Source],
            Config(fixture.Source.Id, AiVideoModel.LiteModelKey)).Value;

        var economy = await fixture.Service.PrepareAsync(WorkflowTestData.Owner, liteDraft, [fixture.Source], default);
        var cached = await fixture.Service.PrepareAsync(WorkflowTestData.Owner, liteDraft, [fixture.Source], default);

        fixture.Requests.Should().HaveCount(4);
        fixture.CacheValues.Should().HaveCount(2);
        economy.Fingerprint.Should().Be(cached.Fingerprint);
        economy.Scenes.Single().AiPromptTrace!.Preparation!.Status.Should().Be("prepared");
        var shot = economy.AiShots!.Single();
        shot.ModelKey.Should().Be(AiVideoModel.LiteModelKey);
        shot.Model.Should().Be(AiVideoModel.LiteModelName);
        shot.PricePerSecondUsd.Should().Be(AiVideoModel.LitePricePerSecondUsd);
        shot.Fingerprint.Should().NotBe(premium.AiShots!.Single().Fingerprint);
        economy.Scenes.Single().AiModelKey.Should().Be(AiVideoModel.LiteModelKey);
    }

    [TestMethod]
    public async Task PrepareAsync_ChangedCropOrDraft_InvalidatesCacheAndPassesActualCrop()
    {
        var fixture = new PreparationFixture();
        await fixture.PrepareAsync();
        var crop = new Region(.1, .2, .6, .5);
        var shot = fixture.Board.AiShots!.Single() with { SourceCrop = crop };
        var cropped = fixture.Board with { AiShots = [shot] };
        await fixture.Service.PrepareAsync(WorkflowTestData.Owner, cropped, [fixture.Source], default);
        fixture.Frames.Verify(frame => frame.PrepareAsync("https://private.test/frame", "9:16", crop,
            It.IsAny<CancellationToken>()), Times.Once);
        var scene = fixture.Board.Scenes.Single();
        var modified = scene.AiPromptTrace! with { FinalPrompt = scene.CompiledPrompt + " Extra creative direction." };
        await fixture.Service.PrepareAsync(WorkflowTestData.Owner,
            fixture.Board with { Scenes = [scene with { AiPromptTrace = modified }] }, [fixture.Source], default);
        fixture.Requests.Should().HaveCount(6);
        fixture.CacheValues.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task PrepareAsync_AlreadyPreparedBoard_IsIdempotent()
    {
        var fixture = new PreparationFixture();
        var first = await fixture.PrepareAsync();
        var second = await fixture.Service.PrepareAsync(WorkflowTestData.Owner, first, [fixture.Source], default);
        second.Should().BeEquivalentTo(first);
        fixture.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("{}")]
    [DataRow("{\"openingDescription\":\"A valid opening caption describes a printed shirt.\",\"uncertainties\":null}")]
    public async Task PrepareAsync_InvalidAnalysis_RetainsExactDraftAndSkipsRewrite(string response)
    {
        var fixture = new PreparationFixture(analysis: response);
        var result = await fixture.PrepareAsync();
        AssertFallback(fixture.Board, result);
        fixture.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("[Shot 2] The camera cuts to a completely different person.")]
    [DataRow("overall_soundscape: New music replaces the scene's ambient sound.")]
    [DataRow("Product: T-shirt. P1 action plan: perform unrelated actions.")]
    public async Task PrepareAsync_InvalidRewrite_RetainsExactDraft(string response)
    {
        var fixture = new PreparationFixture(rewrite: response == "not-json" ? response :
            JsonSerializer.Serialize(new { continuationDescription = response }));
        AssertFallback(fixture.Board, await fixture.PrepareAsync());
        fixture.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task PrepareAsync_TooLongRewrite_RetainsDraftWithoutTruncatingConstraints()
    {
        var fixture = new PreparationFixture(rewrite: JsonSerializer.Serialize(new { continuationDescription = new string('x', 3501) }));
        AssertFallback(fixture.Board, await fixture.PrepareAsync());
    }

    [TestMethod]
    public async Task PrepareAsync_FinalPromptOverProviderLimit_RetainsDraftInsteadOfDroppingGuardrails()
    {
        var fixture = new PreparationFixture(
            analysis: JsonSerializer.Serialize(new { openingDescription = new string('a', 2000), uncertainties = Array.Empty<string>() }),
            rewrite: JsonSerializer.Serialize(new { continuationDescription = new string('b', 3500) }));
        var scene = fixture.Board.Scenes.Single();
        var trace = scene.AiPromptTrace!;
        var parts = trace.Parts.Select(part => part.Key == "product_guardrails"
            ? part with { Content = part.Content + new string('c', 2000) } : part).ToArray();
        var draft = trace with { Parts = parts, FinalPrompt = string.Join("\n\n", parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Content)) };
        var board = fixture.Board with
        {
            Scenes = [scene with { AiPromptTrace = draft, CompiledPrompt = draft.FinalPrompt }],
            AiShots = [fixture.Board.AiShots!.Single() with { Prompt = draft.FinalPrompt }]
        };
        draft.FinalPrompt.Length.Should().BeLessThan(7000);
        AssertFallback(board, await fixture.Service.PrepareAsync(WorkflowTestData.Owner, board, [fixture.Source], default));
    }

    [TestMethod]
    public async Task PrepareAsync_CacheUnavailable_StillPreparesWithoutDiscardingResult()
    {
        var fixture = new PreparationFixture();
        fixture.Cache.Setup(cache => cache.GetAsync<AiPromptTrace>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("offline"));
        fixture.Cache.Setup(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<AiPromptTrace>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("offline"));
        var result = await fixture.PrepareAsync();
        result.Scenes.Single().AiPromptTrace!.Preparation!.Status.Should().Be("prepared");
        result.AiShots!.Single().Prompt.Should().Contain(Opening).And.Contain(Continuation);
    }

    [TestMethod]
    public async Task PrepareAsync_P1Rules_SendsExplicitPlanWithoutDefaultStepToEditor()
    {
        var fixture = new PreparationFixture();
        var product = new Product { Id = fixture.Source.ProductId, ProductType = "tshirt" };
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 6,
            AiScenes: [new(MockupId: fixture.Source.Id, TemplateCode: "lifestyle_in_use", DurationSeconds: 6,
                RightsConfirmed: true, AiVideoModelKey: AiVideoModel.ModelKey, PromptRuleVersion: 1,
                StartingState: "standing with both hands at the sides", PrimaryAction: "lower the shoulders once",
                EndingState: "standing relaxed in the same position")]);
        var board = new VideoStoryboardPlanner().Plan(product, [fixture.Source], config).Value;

        await fixture.Service.PrepareAsync(WorkflowTestData.Owner, board, [fixture.Source], default);

        fixture.Requests[1].Prompt.Should().Contain("standing with both hands at the sides")
            .And.Contain("lower the shoulders once").And.Contain("standing relaxed in the same position")
            .And.NotContain("right foot").And.NotContain("takes one short");
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public async Task PrepareAsync_ProviderUnavailable_RetainsDraftAndCachesShortFallback(string modelKey)
    {
        var fixture = new PreparationFixture(modelKey: modelKey);
        fixture.Generator.Setup(generator => generator.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unavailable"));
        AssertFallback(fixture.Board, await fixture.PrepareAsync());
        AssertFallback(fixture.Board, await fixture.PrepareAsync());
        fixture.Generator.Verify(generator => generator.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Cache.Verify(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<AiPromptTrace>(), TimeSpan.FromMinutes(2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PrepareAsync_CallerCancelsDuringAnalysis_PropagatesCancellationWithoutFallbackCache()
    {
        var fixture = new PreparationFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Generator.Setup(generator => generator.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<GeminiTextGenerationRequest, CancellationToken>((_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Cancellation should have propagated.");
            });
        var action = () => fixture.PrepareAsync(cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Cache.Verify(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<AiPromptTrace>(), It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow("disabled")]
    [DataRow("legacy")]
    [DataRow("other_provider")]
    [DataRow("standard")]
    [DataRow("stale")]
    public async Task PrepareAsync_IneligibleScene_DoesNotReadImageOrCallGenerator(string mode)
    {
        var fixture = new PreparationFixture();
        var board = fixture.Board;
        if (mode == "disabled") fixture.Settings.Enabled = false;
        if (mode == "legacy") board = board with { AiShots = [board.AiShots!.Single() with { ModelKey = VeoVideoModel.ModelKey }] };
        if (mode == "other_provider") board = board with { AiShots = [board.AiShots!.Single() with { ProviderKey = "other" }] };
        if (mode == "standard") board = board with { AiShots = [] };
        if (mode == "stale") fixture.Source.MetadataRevision++;
        var result = await fixture.Service.PrepareAsync(WorkflowTestData.Owner, board, [fixture.Source], default);
        result.Fingerprint.Should().Be(board.Fingerprint);
        fixture.Frames.Verify(frame => frame.PrepareAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Region?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Generator.Verify(generator => generator.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PreviewAsync_ValidPlan_PreparesPromptBeforeReturningReview()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("tshirt");
        var source = fixture.AddAsset(product, "Lifestyle");
        var current = new Mock<ICurrentUser>();
        current.SetupGet(user => user.UserId).Returns(WorkflowTestData.Owner);
        var preparation = new Mock<IVideoPromptPreparationService>();
        preparation.Setup(service => service.PrepareAsync(WorkflowTestData.Owner, It.IsAny<Storyboard>(),
            It.Is<IReadOnlyList<MockupImage>>(sources => sources.Any(item => item.Id == source.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Storyboard board, IReadOnlyList<MockupImage> _, CancellationToken _) => board with { Fingerprint = "prepared-preview" });
        var service = new VideoPlanningService(current.Object, fixture.Products.Object, fixture.Assets.Object,
            fixture.Templates.Object, fixture.CharacterModels.Object, new(), fixture.Capabilities,
            new(), promptPreparation: preparation.Object);
        using var cancellation = new CancellationTokenSource();
        var result = await service.PreviewAsync(product.Id, new(Config(source.Id), [source.Id], null), cancellation.Token);
        result.IsSuccess.Should().BeTrue();
        result.Value.Fingerprint.Should().Be("prepared-preview");
        preparation.Verify(service => service.PrepareAsync(WorkflowTestData.Owner, It.IsAny<Storyboard>(),
            It.IsAny<IReadOnlyList<MockupImage>>(), cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task PlanAsync_NewPlan_PreparesPromptButReusesConfirmedSnapshotWithoutAnotherCall()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct("tshirt");
        var source = fixture.AddAsset(product, "Lifestyle");
        var run = fixture.AddRun(product);
        var config = Config(source.Id);
        var preparation = new Mock<IVideoPromptPreparationService>();
        preparation.Setup(service => service.PrepareAsync(WorkflowTestData.Owner, It.IsAny<Storyboard>(),
            It.IsAny<IReadOnlyList<MockupImage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Storyboard board, IReadOnlyList<MockupImage> _, CancellationToken _) => board with { Fingerprint = "prepared-runtime" });
        var runtime = new WorkflowRuntime(fixture.Nodes.Object, fixture.Products.Object, fixture.Assets.Object,
            fixture.Videos.Object, fixture.Scenes.Object, fixture.Templates.Object, fixture.Jobs.Object,
            fixture.Exports.Object, fixture.Items.Object, new(), fixture.ListingContent.Object, fixture.Time,
            promptPreparation: preparation.Object);
        using var cancellation = new CancellationTokenSource();
        var first = await runtime.PlanAsync(run, config, cancellation.Token);
        first.IsSuccess.Should().BeTrue();
        first.Value.Fingerprint.Should().Be("prepared-runtime");
        fixture.NodeRows.Single(node => node.WorkflowRunId == run.Id && node.NodeType == "generate-video").OutputSnapshot =
            WorkflowJson.Write(new AiShotPromptSnapshot(WorkflowJson.Hash(WorkflowJson.Write(config)), first.Value));

        var second = await runtime.PlanAsync(run, config, cancellation.Token);

        second.Value.Should().BeEquivalentTo(first.Value);
        preparation.Verify(service => service.PrepareAsync(WorkflowTestData.Owner, It.IsAny<Storyboard>(),
            It.IsAny<IReadOnlyList<MockupImage>>(), cancellation.Token), Times.Once);
    }

    [TestMethod]
    [DataRow("fabric_detail", AiVideoModel.ModelKey, "fabric_detail_guardrails", "single product detail")]
    [DataRow("fabric_detail", AiVideoModel.LiteModelKey, "fabric_detail_guardrails", "single product detail")]
    [DataRow("brand_focus", AiVideoModel.ModelKey, "brand_focus_guardrails", "composition anchor")]
    [DataRow("brand_focus", AiVideoModel.LiteModelKey, "brand_focus_guardrails", "composition anchor")]
    public async Task PrepareAsync_DetailOrBrand_RetainsSpecificGuardrailsAndGroundedShot(string subtemplate,
        string modelKey, string guardrailKey, string brief)
    {
        var camera = subtemplate == "fabric_detail"
            ? "arcs around the model toward frame-right with small amplitude at slow speed"
            : "pushes in with small amplitude at slow speed";
        var continuation = subtemplate == "fabric_detail"
            ? $"The camera {camera}, keeping its height, distance and detail scale approximately constant. Restrained background parallax changes as the same product region stays in focus, ending slightly more side-on without revealing the back."
            : $"The camera {camera} as the model gently lifts their gaze upward, followed by a small, relaxed lift of the chin. The model settles looking slightly above the lens with natural breathing while the visible face and design remain within the frame and clear.";
        var fixture = new PreparationFixture(subtemplateCode: subtemplate, modelKey: modelKey,
            rewrite: JsonSerializer.Serialize(new { continuationDescription = continuation }));

        var result = await fixture.PrepareAsync();

        fixture.Requests.Should().HaveCount(2);
        fixture.Requests[0].Images!.Single().Bytes.Should().Equal(fixture.Pixels);
        fixture.Requests[1].SystemInstruction.Should().Contain(brief)
            .And.Contain(camera);
        var trace = result.Scenes.Single().AiPromptTrace!;
        trace.Preparation!.Status.Should().Be("prepared");
        trace.FinalPrompt.Should().Contain(Opening + " " + continuation)
            .And.Contain(fixture.Board.Scenes.Single().AiPromptTrace!.Parts.Single(part => part.Key == guardrailKey).Content);
        trace.Parts.Single(part => part.Key == "retained_" + guardrailKey).IncludedInFinalPrompt.Should().BeTrue();
        if (subtemplate == "brand_focus")
        {
            fixture.Requests[1].SystemInstruction.Should().Contain("gently lifts their gaze upward")
                .And.Contain("a small, relaxed lift of the chin");
            trace.Parts.Single(part => part.Key == "retained_" + guardrailKey).Content.Should()
                .Contain("gently lifts their gaze upward").And.Contain("a small, relaxed lift of the chin");
        }
        var shot = result.AiShots!.Single();
        shot.Prompt.Should().Be(trace.FinalPrompt);
        shot.ModelKey.Should().Be(modelKey);
        shot.PricePerSecondUsd.Should().Be(fixture.Board.AiShots!.Single().PricePerSecondUsd);
        result.Fingerprint.Should().NotBe(fixture.Board.Fingerprint);
    }

    [TestMethod]
    [DataRow("fabric_detail", AiVideoModel.ModelKey)]
    [DataRow("fabric_detail", AiVideoModel.LiteModelKey)]
    [DataRow("brand_focus", AiVideoModel.ModelKey)]
    [DataRow("brand_focus", AiVideoModel.LiteModelKey)]
    public async Task PrepareAsync_DetailOrBrandRewriteLosesCamera_RetainsExactCompilerDraft(string subtemplate, string modelKey)
    {
        var fixture = new PreparationFixture(subtemplateCode: subtemplate, modelKey: modelKey,
            rewrite: JsonSerializer.Serialize(new { continuationDescription = "The camera races around the model and reveals a new product detail on the back." }));

        AssertFallback(fixture.Board, await fixture.PrepareAsync());
        fixture.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, "The camera pushes in with small amplitude at slow speed toward the same visible fabric detail.")]
    [DataRow(AiVideoModel.LiteModelKey, "The camera arcs around the model toward frame-right with small amplitude at fast speed.")]
    [DataRow(AiVideoModel.ModelKey, "The camera arcs around the model toward frame-right with small amplitude at slow speed, then the camera pushes in toward the print.")]
    [DataRow(AiVideoModel.LiteModelKey, "The camera arcs around the model toward frame-right with small amplitude at slow speed, then continues with large amplitude.")]
    public async Task PrepareAsync_FabricRewriteChangesArc_RetainsExactCompilerDraft(string modelKey, string continuation)
    {
        var fixture = new PreparationFixture(subtemplateCode: "fabric_detail", modelKey: modelKey,
            rewrite: JsonSerializer.Serialize(new { continuationDescription = continuation }));

        var result = await fixture.PrepareAsync();

        AssertFallback(fixture.Board, result);
        result.AiShots!.Single().Prompt.Should().Contain("arcs around the model toward frame-right with small amplitude at slow speed")
            .And.NotContain("camera pushes in");
        fixture.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey, "The camera pushes in with small amplitude at slow speed while the model stays still and breathes gently.")]
    [DataRow(AiVideoModel.LiteModelKey, "The camera pushes in with small amplitude at slow speed while the model gently lifts their gaze upward and remains in place.")]
    [DataRow(AiVideoModel.ModelKey, "The camera pushes in with small amplitude at slow speed while the model makes a small, relaxed lift of the chin.")]
    public async Task PrepareAsync_BrandRewriteDropsUpwardPerformance_RetainsExactCompilerDraft(string modelKey, string continuation)
    {
        var fixture = new PreparationFixture(subtemplateCode: "brand_focus", modelKey: modelKey,
            rewrite: JsonSerializer.Serialize(new { continuationDescription = continuation }));

        var result = await fixture.PrepareAsync();

        AssertFallback(fixture.Board, result);
        result.AiShots!.Single().Prompt.Should().Contain("gently lifts their gaze upward")
            .And.Contain("a small, relaxed lift of the chin")
            .And.Contain("pushes in with small amplitude at slow speed");
        fixture.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    [DataRow(AiVideoModel.ModelKey)]
    [DataRow(AiVideoModel.LiteModelKey)]
    public async Task PrepareAsync_BrandWithP1_PreservesExplicitPerformanceWithoutDefaultGaze(string modelKey)
    {
        const string continuation = "The camera pushes in with small amplitude at slow speed while the model lowers the free hand once and finishes with that hand resting at the side.";
        var fixture = new PreparationFixture(subtemplateCode: "brand_focus", modelKey: modelKey,
            rewrite: JsonSerializer.Serialize(new { continuationDescription = continuation }));
        var product = new Product { Id = fixture.Source.ProductId, ProductType = "tshirt" };
        var board = new VideoStoryboardPlanner().Plan(product, [fixture.Source], new(Mode: "ai_shot", DurationSeconds: 6,
            AiScenes: [new(MockupId: fixture.Source.Id, TemplateCode: "lifestyle_in_use", SubtemplateCode: "brand_focus",
                DurationSeconds: 6, RightsConfirmed: true, AiVideoModelKey: modelKey, PromptRuleVersion: 1,
                StartingState: "the free hand is raised", PrimaryAction: "lower the free hand once",
                EndingState: "the free hand rests at the side")])).Value;

        var result = await fixture.Service.PrepareAsync(WorkflowTestData.Owner, board, [fixture.Source], default);

        var trace = result.Scenes.Single().AiPromptTrace!;
        trace.Preparation!.Status.Should().Be("prepared");
        trace.RuleVersion.Should().Be(1);
        trace.FinalPrompt.Should().Contain(continuation).And.NotContain("gently lifts their gaze upward");
        fixture.Requests[1].Prompt.Should().Contain("lower the free hand once").And.NotContain("gently lifts their gaze upward");
        fixture.Requests[1].SystemInstruction.Should().NotContain("Use 'gently lifts their gaze upward'");
        result.AiShots!.Single().Prompt.Should().Be(trace.FinalPrompt);
    }

    private static void AssertFallback(Storyboard original, Storyboard actual)
    {
        actual.AiShots.Should().BeEquivalentTo(original.AiShots);
        actual.Fingerprint.Should().Be(original.Fingerprint);
        var trace = actual.Scenes.Single().AiPromptTrace!;
        trace.FinalPrompt.Should().Be(original.Scenes.Single().CompiledPrompt);
        trace.Preparation!.Status.Should().Be("fallback");
        trace.Warnings.Should().Contain(warning => warning.Contains("deterministic H3 prompt was retained"));
    }

    private static GenerateVideoConfig Config(Guid sourceId, string modelKey = AiVideoModel.ModelKey,
        string subtemplateCode = "natural_motion") => new(Mode: "ai_shot", DurationSeconds: 6,
        AiScenes: [new(MockupId: sourceId, TemplateCode: "lifestyle_in_use", SubtemplateCode: subtemplateCode,
            DurationSeconds: 6, RightsConfirmed: true, AiVideoModelKey: modelKey)]);

    private sealed class PreparationFixture
    {
        internal Mock<IGeminiTextGenerationProvider> Generator { get; } = new();
        internal Mock<IVideoFirstFramePreparer> Frames { get; } = new();
        internal Mock<ICacheService> Cache { get; } = new();
        internal Dictionary<string, AiPromptTrace> CacheValues { get; } = [];
        internal List<GeminiTextGenerationRequest> Requests { get; } = [];
        internal byte[] Pixels { get; } = [1, 2, 3, 4];
        internal VideoPromptPreparationOptions Settings { get; } = new();
        internal VideoPromptPreparationService Service { get; }
        internal Storyboard Board { get; }
        internal MockupImage Source { get; }

        internal PreparationFixture(string? analysis = null, string? rewrite = null, string modelKey = AiVideoModel.ModelKey,
            string subtemplateCode = "natural_motion")
        {
            var fixture = new WorkflowTestData();
            var product = fixture.AddProduct("tshirt");
            Source = fixture.AddAsset(product, "Lifestyle");
            Board = new VideoStoryboardPlanner().Plan(product, [Source], Config(Source.Id, modelKey, subtemplateCode)).Value;
            fixture.Storage.Setup(storage => storage.SignRead(Source.StorageKey, "image", Source.StorageVersion, false))
                .Returns("https://private.test/frame");
            Frames.Setup(frame => frame.PrepareAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Pixels);
            Generator.Setup(generator => generator.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GeminiTextGenerationRequest request, CancellationToken _) =>
                {
                    Requests.Add(request);
                    return request.Images is not null
                        ? new(analysis ?? JsonSerializer.Serialize(new { openingDescription = Opening, uncertainties = new[] { "Fine lettering is unreadable." } }), "vision-model", 10, 20, null)
                        : new(rewrite ?? JsonSerializer.Serialize(new { continuationDescription = Continuation }), "editor-model", 20, 30, null);
                });
            Cache.Setup(cache => cache.GetAsync<AiPromptTrace>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken _) => CacheValues.GetValueOrDefault(key));
            Cache.Setup(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<AiPromptTrace>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback((string key, AiPromptTrace value, TimeSpan? _, CancellationToken _) => CacheValues[key] = value)
                .Returns(Task.CompletedTask);
            Service = new(Generator.Object, Frames.Object, fixture.Storage.Object, Cache.Object, Options.Create(Settings),
                NullLogger<VideoPromptPreparationService>.Instance);
        }

        internal Task<Storyboard> PrepareAsync(CancellationToken ct = default) =>
            Service.PrepareAsync(WorkflowTestData.Owner, Board, [Source], ct);
    }
}
