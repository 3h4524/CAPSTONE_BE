using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

/// <summary>
/// With Video:DeferredRender on, Generate Video ends once the scenes are ready and the final video is rendered later, in the
/// background, only when a step needs the file.
/// </summary>
[TestClass]
public sealed class DeferredRenderTests
{
    private static readonly DeferredRenderSettings On = new() { Enabled = true };

    /// <summary>A run waiting at Review Video whose video is ready to be shown (no file), with its render job waiting to be asked for.</summary>
    private static WorkflowTestData ReadyFixture(string? exportConfig = null, bool etsyInsteadOfExport = false,
        APCS.Application.Features.Entitlements.IPlanEntitlementService? plan = null)
    {
        var fixture = new WorkflowTestData(deferredRender: On, runtimeEntitlements: plan);
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        var board = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, new()).Value;
        video.QaResult = "{}"; video.Status = "ready"; video.StorageKey = null; video.StorageVersion = null; video.ThumbnailStorageKey = null; video.ThumbnailStorageVersion = null;
        video.ConfigSnapshot = WorkflowJson.Write(new { config = new GenerateVideoConfig(), storyboard = board });
        fixture.SceneRows.Single().SceneConfig = WorkflowJson.Write(board.Scenes[0]);
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        fixture.JobRows.Add(new MediaJob { Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner, WorkflowRunId = run.Id, WorkflowNodeRunId = generate.Id,
            Kind = "render", Status = DeferredRender.JobStatus, MaximumAttempts = 3, Payload = WorkflowJson.Write(new RenderJobPayload(video.Id, board)) });
        var export = fixture.NodeRows.Single(x => x.NodeType == "export-zip");
        if (exportConfig is not null) export.InputSnapshot = exportConfig;
        if (etsyInsteadOfExport)
        {
            fixture.NodeRows.Remove(export);
            fixture.NodeRows.Add(new() { Id = Guid.NewGuid(), WorkflowRunId = run.Id, NodeId = "etsy", NodeType = "publish-etsy", Status = "pending", Attempt = 1,
                InputSnapshot = """{"includeVideo":true}""", OutputSnapshot = "{}", CreatedAt = WorkflowTestData.Now.UtcDateTime, UpdatedAt = WorkflowTestData.Now.UtcDateTime });
        }
        return fixture;
    }

    private static Task<APCS.Common.Models.Result<RunResponse>> Approve(WorkflowTestData fixture) =>
        fixture.RunService.ActionAsync(fixture.RunRows.Single().Id, new(7, "approve_video", fixture.VideoRows.Single().Id, 2), default);

    private static void SetupVerification(WorkflowTestData fixture) =>
        fixture.Storage.Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

    /// <summary>The worker has the render job and finishes it.</summary>
    private static async Task<bool> FinishRenderAsync(WorkflowTestData fixture)
    {
        var job = fixture.JobRows.Single(x => x.Kind == "render");
        job.Status = "leased"; job.LeaseToken = Guid.NewGuid(); job.LeaseExpiresAt = WorkflowTestData.Now.AddMinutes(2).UtcDateTime; job.Attempt = 1;
        SetupVerification(fixture);
        var result = await fixture.MediaService.CompleteAsync(job.Id, new(job.LeaseToken!.Value, WorkflowTestData.OutputKey(job, "video"), "1",
            WorkflowTestData.OutputKey(job, "thumbnail"), "1", WorkflowTestData.Qa()), default);
        return result.IsSuccess;
    }

    // ---- which steps need the final video ----

    [TestMethod]
    [DataRow("{}", true)]
    [DataRow("""{"includeFinalVideo":true}""", true)]
    [DataRow("""{"includeFinalVideo":false}""", false)]
    [DataRow("not json", true)]
    public void ExportNeedsFinalVideo_ReadsTheOptionAndDefaultsToOn(string config, bool expected)
    {
        var export = new WorkflowNodeRun { NodeType = "export-zip", InputSnapshot = config };

        VideoRenderDemand.ExportNeedsFinalVideo(export).Should().Be(expected);
    }

    [TestMethod]
    public void Requires_WhenNeitherStepWantsTheFile_IsFalse()
    {
        WorkflowNodeRun[] nodes =
        [
            new() { NodeType = "export-zip", InputSnapshot = """{"includeFinalVideo":false}""" },
            new() { NodeType = "publish-etsy", InputSnapshot = """{"includeVideo":false}""" }
        ];

        VideoRenderDemand.Requires(nodes).Should().BeFalse();
    }

    [TestMethod]
    public void Requires_PublishToEtsyWithAVideo_IsTrueWhateverExportSays()
    {
        WorkflowNodeRun[] nodes =
        [
            new() { NodeType = "export-zip", InputSnapshot = """{"includeFinalVideo":false}""" },
            new() { NodeType = "publish-etsy", InputSnapshot = """{"listingType":"download"}""" }
        ];

        VideoRenderDemand.Requires(nodes).Should().BeTrue();
    }

    // ---- Generate Video ends once the scenes are ready ----

    [TestMethod]
    public async Task AdvanceAsync_StandardVideo_EndsGenerateVideoAtOnceWithoutRendering()
    {
        var fixture = new WorkflowTestData(deferredRender: On);
        var product = fixture.AddProduct();
        fixture.AddAsset(product);
        var run = fixture.AddRun(product);
        foreach (var node in fixture.NodeRows.Take(3)) node.Status = "succeeded";

        await fixture.Runtime.AdvanceAsync(run, default);

        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        generate.Status.Should().Be("succeeded");
        generate.Progress.Should().Be(100);
        fixture.VideoRows.Single().Status.Should().Be(DeferredRender.VideoReady);
        var job = fixture.JobRows.Single();
        job.Kind.Should().Be("render");
        job.Status.Should().Be(DeferredRender.JobStatus);
        // The seller reviews the video that has not been rendered.
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status.Should().Be("waiting_for_review");
        run.Status.Should().Be("waiting_for_review");
    }

    [TestMethod]
    public async Task AdvanceAsync_FlagOff_RendersInsideGenerateVideoAsBefore()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        fixture.AddAsset(product);
        var run = fixture.AddRun(product);
        foreach (var node in fixture.NodeRows.Take(3)) node.Status = "succeeded";

        await fixture.Runtime.AdvanceAsync(run, default);

        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Status.Should().Be("running");
        fixture.JobRows.Single().Status.Should().Be("queued");
        fixture.VideoRows.Single().Status.Should().Be("queued");
    }

    [TestMethod]
    public async Task ClipPhase_EveryClipStored_EndsGenerateVideoAndHoldsTheRenderJob()
    {
        var fixture = new WorkflowTestData(deferredRender: On);
        var product = fixture.AddProduct();
        fixture.AddAsset(product);
        var run = fixture.AddRun(product);
        foreach (var node in fixture.NodeRows.Take(3)) node.Status = "succeeded";
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        generate.Status = "running";
        var video = new PromoVideo { Id = Guid.NewGuid(), ProductId = product.Id, WorkflowRunId = run.Id, Status = "queued", Mode = "ai_shot",
            ApprovalStatus = "pending", ReviewRevision = 1, VersionNumber = 1, SeriesId = Guid.NewGuid() };
        fixture.VideoRows.Add(video);
        generate.OutputSnapshot = WorkflowJson.Write(new VideoPointer(video.Id));
        var job = fixture.AddJob(run);
        job.Status = AiClipPhase.JobStatus; job.LeaseToken = null; job.LeaseExpiresAt = null; job.Attempt = 0;
        var board = new Storyboard("auto", 2, 8, "tshirt", [], "board", Mode: "ai_shot");
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(video.Id, board,
            [new AiShotJobState(0, "f0", "op0", Status: "stored", StorageKey: "k", StorageVersion: "1")]));
        fixture.State.Setup(x => x.FindClipPhaseJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => fixture.JobRows.Where(x => x.Status == AiClipPhase.JobStatus && x.Kind == "render")
                .Select(x => new ClipPhaseJob(x.Id, x.WorkflowRunId, x.UserId, x.Payload, fixture.RunRows.Single(r => r.Id == x.WorkflowRunId).Status)).ToList());
        var service = new AiClipPhaseService(fixture.State.Object, fixture.Unit.Object, new Mock<IAiShotJobService>().Object, fixture.Jobs.Object,
            fixture.Nodes.Object, fixture.Runtime, Microsoft.Extensions.Options.Options.Create(new AiClipPhaseSettings()), fixture.Time);

        await service.PlanAsync(default);

        // The clips are all that Generate Video waited for: it ends, and the job waits until a step needs the video.
        job.Status.Should().Be(DeferredRender.JobStatus);
        generate.Status.Should().Be("succeeded");
        video.Status.Should().Be(DeferredRender.VideoReady);
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status.Should().Be("waiting_for_review");
        run.Status.Should().Be("waiting_for_review");
    }

    [TestMethod]
    [DataRow(false, "queued", "running")]
    [DataRow(true, "generating_clips", "running")]
    public async Task AdvanceAsync_AiVideoWhoseClipsAreNotStored_IsNotEndedEarly(bool clipPhase, string jobStatus, string nodeStatus)
    {
        // Generate Video can only end before the render when every scene is there: an AI clip still to be made keeps it going.
        var fixture = new WorkflowTestData(clipPhase: clipPhase ? new AiClipPhaseSettings { Enabled = true } : null, deferredRender: On);
        var product = fixture.AddProduct();
        fixture.AddAsset(product);
        var run = fixture.AddRun(product);
        foreach (var node in fixture.NodeRows.Take(3)) node.Status = "succeeded";
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        generate.InputSnapshot = WorkflowJson.Write(new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 8, AiShotOptions: new(Coverage: "full_video")));

        await fixture.Runtime.AdvanceAsync(run, default);

        fixture.JobRows.Single().Status.Should().Be(jobStatus);
        generate.Status.Should().Be(nodeStatus);
        fixture.VideoRows.Single().Status.Should().Be("queued");
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status.Should().Be("pending");
    }

    // ---- the render starts when the video is approved, if a step needs it ----

    [TestMethod]
    public async Task Approve_WhenExportWantsTheFinalVideo_QueuesTheRenderAndExportWaitsForIt()
    {
        var fixture = ReadyFixture();

        var result = await Approve(fixture);

        result.IsSuccess.Should().BeTrue();
        var video = fixture.VideoRows.Single();
        video.ApprovalStatus.Should().Be("approved");
        video.Status.Should().Be(DeferredRender.VideoRendering);
        fixture.JobRows.Single().Status.Should().Be("queued");
        var export = fixture.NodeRows.Single(x => x.NodeType == "export-zip");
        export.Status.Should().Be("running");
        export.Stage.Should().Be(DeferredRender.WaitStage);
        DeferredRender.IsWaiting(export).Should().BeTrue();
        fixture.RunRows.Single().Status.Should().Be("running");
        fixture.ExportRows.Should().BeEmpty();
        fixture.JobRows.Should().NotContain(x => x.Kind == "export_zip");
    }

    [TestMethod]
    public async Task Approve_WhenNoStepNeedsTheFile_DoesNotRenderAndExportRunsAtOnce()
    {
        var fixture = ReadyFixture("""{"includeFinalVideo":false}""");

        var result = await Approve(fixture);

        result.IsSuccess.Should().BeTrue();
        fixture.VideoRows.Single().Status.Should().Be(DeferredRender.VideoReady);
        fixture.JobRows.Single(x => x.Kind == "render").Status.Should().Be(DeferredRender.JobStatus);
        fixture.NodeRows.Single(x => x.NodeType == "export-zip").Status.Should().Be("running");
        DeferredRender.IsWaiting(fixture.NodeRows.Single(x => x.NodeType == "export-zip")).Should().BeFalse();
        fixture.ExportRows.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Approve_ExportWithoutTheFinalVideo_PackagesTheScenesOnly()
    {
        var fixture = ReadyFixture("""{"includeFinalVideo":false}""");

        await Approve(fixture);

        var export = WorkflowJson.Read<ExportJobPayload>(fixture.JobRows.Single(x => x.Kind == "export_zip").Payload);
        export.VideoId.Should().Be(fixture.VideoRows.Single().Id);
        export.VideoStorageKey.Should().BeNull();
        export.ThumbnailStorageKey.Should().BeNull();
        // The scenes are made again from the video's own sources, which were read from its mock-ups.
        export.Sources.Should().NotBeNull();
        export.Sources!.Scenes.Should().NotBeEmpty().And.OnlyContain(scene => scene.ImageStorageKey == "private/mockup");
        export.Manifest.GetProperty("video").ValueKind.Should().Be(JsonValueKind.Null);
        export.EtsyGuide.Should().NotContain("video/final");
    }

    [TestMethod]
    public async Task Approve_ReadyVideo_HasNoFileUntilTheRenderFinishes()
    {
        var fixture = ReadyFixture();

        await Approve(fixture);

        // Asked for, not made: it is the render finishing that makes the file.
        fixture.VideoRows.Single().StorageKey.Should().BeNull();
    }

    [TestMethod]
    public async Task Approve_WhenThePlanRefusesTheRenderNow_KeepsTheVideoReadyAndFailsTheWaitingStepWithTheReason()
    {
        // A ready video is not counted against the plan, so the plan is asked when its file is to be made.
        var plan = new Mock<APCS.Application.Features.Entitlements.IPlanEntitlementService>();
        plan.Setup(x => x.RequireAllowanceAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(APCS.Common.Models.Result.Success());
        plan.Setup(x => x.RequireDailyCapacityAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(APCS.Common.Models.Result.Failure(APCS.Common.Models.Error.Validation("You have reached today's video limit.")));
        var fixture = ReadyFixture(plan: plan.Object);

        await Approve(fixture);

        fixture.VideoRows.Single().Status.Should().Be(DeferredRender.VideoReady);
        fixture.JobRows.Single().Status.Should().Be(DeferredRender.JobStatus);
        var export = fixture.NodeRows.Single(x => x.NodeType == "export-zip");
        export.Status.Should().Be("failed");
        export.ErrorMessage.Should().Be("You have reached today's video limit.");
        fixture.RunRows.Single().Status.Should().Be("failed");
    }

    [TestMethod]
    public async Task Approve_WhenThePlanAllowsTheRender_AsksForItOncePerRequest()
    {
        var plan = new Mock<APCS.Application.Features.Entitlements.IPlanEntitlementService>();
        plan.Setup(x => x.RequireAllowanceAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(APCS.Common.Models.Result.Success());
        plan.Setup(x => x.RequireDailyCapacityAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(APCS.Common.Models.Result.Success());
        var fixture = ReadyFixture(plan: plan.Object);

        await Approve(fixture);

        fixture.JobRows.Single().Status.Should().Be("queued");
        // The second request (the step waiting for the file) finds the render already queued and asks nothing more.
        plan.Verify(x => x.RequireAllowanceAsync(It.IsAny<Guid>(), "videos", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- the render finishing lets the waiting step go on ----

    [TestMethod]
    public async Task CompleteAsync_FinalRender_LetsTheWaitingExportGoOn()
    {
        var fixture = ReadyFixture();
        await Approve(fixture);

        var completed = await FinishRenderAsync(fixture);

        completed.Should().BeTrue();
        var video = fixture.VideoRows.Single();
        video.Status.Should().Be(DeferredRender.VideoCompleted);
        video.StorageKey.Should().NotBeNull();
        var export = fixture.NodeRows.Single(x => x.NodeType == "export-zip");
        export.Status.Should().Be("running");
        export.Stage.Should().Be("Exporting");
        DeferredRender.IsWaiting(export).Should().BeFalse();
        var exportJob = fixture.JobRows.Single(x => x.Kind == "export_zip");
        WorkflowJson.Read<ExportJobPayload>(exportJob.Payload).VideoStorageKey.Should().Be(video.StorageKey);
        // Generate Video stays done.
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Status.Should().Be("succeeded");
    }

    [TestMethod]
    public async Task CompleteAsync_FinalRender_KeepsWhatWasSavedBesideTheChecks()
    {
        var fixture = ReadyFixture();
        fixture.VideoRows.Single().QaResult = """{"videoQuality":{"status":"scored"}}""";
        await Approve(fixture);

        await FinishRenderAsync(fixture);

        using var qa = JsonDocument.Parse(fixture.VideoRows.Single().QaResult);
        qa.RootElement.GetProperty("videoQuality").GetProperty("status").GetString().Should().Be("scored");
        qa.RootElement.GetProperty("technical").GetProperty("fullDecode").GetBoolean().Should().BeTrue();
        qa.RootElement.GetProperty("etsyCompliance").GetProperty("status").GetString().Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task CompleteAsync_RenderOfABatchVideoStillWaitingForContinue_LeavesTheRunAlone()
    {
        var fixture = ReadyFixture();
        var run = fixture.RunRows.Single();
        run.RunGroupId = Guid.NewGuid();
        await Approve(fixture);
        // In a batch the decision can still change, so the run waits for Continue and the render goes on beside it.
        run.Status.Should().Be("waiting_for_review");
        fixture.JobRows.Single().Status.Should().Be("queued");

        var completed = await FinishRenderAsync(fixture);

        completed.Should().BeTrue();
        fixture.VideoRows.Single().Status.Should().Be(DeferredRender.VideoCompleted);
        run.Status.Should().Be("waiting_for_review");
        fixture.JobRows.Should().NotContain(x => x.Kind == "export_zip");
    }

    // ---- Publish to Etsy always needs the video ----

    [TestMethod]
    public async Task Approve_PublishToEtsyWithAVideo_WaitsForTheRenderThenQueuesThePublish()
    {
        var fixture = ReadyFixture(etsyInsteadOfExport: true);

        await Approve(fixture);

        var etsy = fixture.NodeRows.Single(x => x.NodeType == "publish-etsy");
        etsy.Status.Should().Be("running");
        DeferredRender.IsWaiting(etsy).Should().BeTrue();
        fixture.JobRows.Single().Status.Should().Be("queued");

        await FinishRenderAsync(fixture);

        etsy.Status.Should().Be("running");
        DeferredRender.IsWaiting(etsy).Should().BeFalse();
        etsy.Stage.Should().Be("Queued");
    }

    // ---- the browser plays a video that has no file ----

    [TestMethod]
    public async Task ListAsync_ReadyVideo_CarriesWhatTheBrowserNeedsToPlayItFromItsScenes()
    {
        var fixture = ReadyFixture();
        fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>())).Returns("https://signed.example/media");

        var result = await fixture.ArtifactService.ListAsync(fixture.RunRows.Single().Id, default);

        var video = result.Value.Single();
        video.PreviewUrl.Should().BeNull();
        video.Preview.Should().NotBeNull();
        video.Preview!.Storyboard.Scenes.Should().NotBeEmpty();
        video.Preview.Sources.Keys.Should().BeEquivalentTo(video.Preview.Storyboard.Scenes.Select(scene => scene.MockupId).Distinct());
        video.Preview.Sources.Values.Should().OnlyContain(source => source.Url == "https://signed.example/media" && source.Width > 0 && source.Height > 0);
        video.Preview.Clips.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ListAsync_RenderedVideo_PlaysFromItsFileAndCarriesNoPreviewData()
    {
        var fixture = ReadyFixture();
        fixture.VideoRows.Single().Status = "completed";
        fixture.VideoRows.Single().StorageKey = "private/video";
        fixture.VideoRows.Single().StorageVersion = "1";
        fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>())).Returns("https://signed.example/media");

        var result = await fixture.ArtifactService.ListAsync(fixture.RunRows.Single().Id, default);

        result.Value.Single().Preview.Should().BeNull();
        result.Value.Single().PreviewUrl.Should().NotBeNull();
    }

    // ---- failure and stopping ----

    [TestMethod]
    public async Task FailAsync_FinalRenderThatCannotBeRetried_FailsTheWaitingStepAndPutsTheVideoBack()
    {
        var fixture = ReadyFixture();
        await Approve(fixture);
        var job = fixture.JobRows.Single();
        job.Status = "leased"; job.LeaseToken = Guid.NewGuid(); job.LeaseExpiresAt = WorkflowTestData.Now.AddMinutes(2).UtcDateTime; job.Attempt = 1;

        await fixture.MediaService.FailAsync(job.Id, new(job.LeaseToken!.Value, "SourceChanged"), default);

        job.Status.Should().Be("failed");
        var export = fixture.NodeRows.Single(x => x.NodeType == "export-zip");
        export.Status.Should().Be("failed");
        export.ErrorMessage.Should().Contain("no longer approved");
        fixture.RunRows.Single().Status.Should().Be("failed");
        // Generate Video is still done, and the approved video is ready to render again.
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Status.Should().Be("succeeded");
        fixture.VideoRows.Single().Status.Should().Be(DeferredRender.VideoReady);
        fixture.VideoRows.Single().ApprovalStatus.Should().Be("approved");
    }

    [TestMethod]
    public async Task RetryAfterAFailedFinalRender_AsksForTheRenderAgain()
    {
        var fixture = ReadyFixture();
        await Approve(fixture);
        var job = fixture.JobRows.Single();
        job.Status = "leased"; job.LeaseToken = Guid.NewGuid(); job.LeaseExpiresAt = WorkflowTestData.Now.AddMinutes(2).UtcDateTime; job.Attempt = 1;
        await fixture.MediaService.FailAsync(job.Id, new(job.LeaseToken!.Value, "SourceChanged"), default);
        var run = fixture.RunRows.Single();
        var export = fixture.NodeRows.Single(x => x.NodeType == "export-zip");
        export.Status = "pending"; run.Status = "running";

        await fixture.Runtime.AdvanceAsync(run, default);

        job.Status.Should().Be("queued");
        job.Attempt.Should().Be(0);
        job.ErrorMessage.Should().BeNull();
        fixture.VideoRows.Single().Status.Should().Be(DeferredRender.VideoRendering);
        DeferredRender.IsWaiting(export).Should().BeTrue();
    }

    [TestMethod]
    public async Task EndUnfinishedVideosAsync_KeepsAReadyVideoAndPutsARenderingOneBack()
    {
        var fixture = ReadyFixture();
        var ready = fixture.VideoRows.Single();
        var rendering = new PromoVideo { Id = Guid.NewGuid(), WorkflowRunId = fixture.RunRows.Single().Id, Status = "rendering" };
        var making = new PromoVideo { Id = Guid.NewGuid(), WorkflowRunId = fixture.RunRows.Single().Id, Status = "queued" };
        fixture.VideoRows.AddRange([rendering, making]);

        await WorkflowRunStops.EndUnfinishedVideosAsync(fixture.Videos.Object, fixture.RunRows.Single().Id, default, generateEnded: true);

        ready.Status.Should().Be(DeferredRender.VideoReady);
        rendering.Status.Should().Be(DeferredRender.VideoReady);
        making.Status.Should().Be("failed");
    }

    [TestMethod]
    public async Task EndUnfinishedVideosAsync_ARenderingVideoOfAStepThatDidNotEnd_IsFailedAsBefore()
    {
        var fixture = ReadyFixture();
        var rendering = new PromoVideo { Id = Guid.NewGuid(), WorkflowRunId = fixture.RunRows.Single().Id, Status = "rendering" };
        fixture.VideoRows.Add(rendering);

        await WorkflowRunStops.EndUnfinishedVideosAsync(fixture.Videos.Object, fixture.RunRows.Single().Id, default);

        rendering.Status.Should().Be("failed");
    }

    [TestMethod]
    public async Task Cancel_AReadyVideosRun_CancelsItsWaitingRenderAndKeepsTheVideo()
    {
        var fixture = ReadyFixture();
        var run = fixture.RunRows.Single();

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "cancel"), default);

        result.IsSuccess.Should().BeTrue();
        fixture.JobRows.Single().Status.Should().Be("cancelled");
        fixture.VideoRows.Single().Status.Should().Be(DeferredRender.VideoReady);
    }

    // ---- the heartbeat of a render beside a waiting run ----

    [TestMethod]
    public async Task HeartbeatAsync_RenderWhileTheRunWaitsForReview_KeepsTheLease()
    {
        var fixture = ReadyFixture();
        fixture.RunRows.Single().RunGroupId = Guid.NewGuid();
        await Approve(fixture);
        var job = fixture.JobRows.Single();
        job.Status = "leased"; job.LeaseToken = Guid.NewGuid(); job.LeaseExpiresAt = WorkflowTestData.Now.AddMinutes(2).UtcDateTime;

        var result = await fixture.MediaService.HeartbeatAsync(job.Id, new(job.LeaseToken!.Value, "Rendering", 40), default);

        result.IsSuccess.Should().BeTrue();
        fixture.RunRows.Single().Status.Should().Be("waiting_for_review");
    }

    [TestMethod]
    public void Works_ExportNeedsARunningRun_ButARenderMayWaitBesideAReview()
    {
        MediaJobRunRules.Works("export_zip", "running").Should().BeTrue();
        MediaJobRunRules.Works("export_zip", "waiting_for_review").Should().BeFalse();
        MediaJobRunRules.Works("render", "waiting_for_review").Should().BeTrue();
        MediaJobRunRules.Works("render", "queued").Should().BeTrue();
        MediaJobRunRules.Works("render", "cancelled").Should().BeFalse();
        MediaJobRunRules.Works("render", "failed").Should().BeFalse();
    }

    // ---- auto approval of a video that has no file ----

    [TestMethod]
    public void Approve_AutoApprovesAReadyVideoWhoseStoryboardCanBeListed()
    {
        var fixture = ReadyFixture();
        var video = fixture.VideoRows.Single();
        video.ApprovalStatus = "pending";

        var approved = VideoAutoApproval.Approve(video, WorkflowTestData.Owner, WorkflowTestData.Now.UtcDateTime);

        approved.Should().BeTrue();
        video.ApprovalStatus.Should().Be("approved");
    }

    [TestMethod]
    public void Approve_DoesNotAutoApproveAReadyVideoWhoseStoryboardBreaksEtsyRules()
    {
        var fixture = ReadyFixture();
        var video = fixture.VideoRows.Single();
        // Etsy's listing videos are 5–15 seconds long.
        var board = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, new()).Value with { OutputDurationFrames = 600 };
        video.ConfigSnapshot = WorkflowJson.Write(new { config = new GenerateVideoConfig(), storyboard = board });

        VideoAutoApproval.Approve(video, WorkflowTestData.Owner, WorkflowTestData.Now.UtcDateTime).Should().BeFalse();
        video.ApprovalStatus.Should().Be("pending");
    }

    [TestMethod]
    public void Approve_DoesNotAutoApproveAVideoStillBeingMade()
    {
        var fixture = ReadyFixture();
        var video = fixture.VideoRows.Single();
        video.Status = "queued";

        VideoAutoApproval.Approve(video, WorkflowTestData.Owner, WorkflowTestData.Now.UtcDateTime).Should().BeFalse();
    }
}
