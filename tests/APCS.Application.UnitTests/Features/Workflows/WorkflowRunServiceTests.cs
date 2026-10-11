using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowRunServiceTests
{
    [TestMethod]
    public async Task ActionAsync_InvalidRerenderConfiguration_DoesNotCreateVersion()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "rerender", Config: new(DurationSeconds: 16)), default);
        result.IsFailure.Should().BeTrue();
        fixture.VideoRows.Should().HaveCount(1);
        fixture.JobRows.Should().BeEmpty();
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Attempt.Should().Be(1);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_ForeignWorkflow_DoesNotCreateRunEvenWhenProductIsOwned()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct()); workflow.UserId = Guid.NewGuid();
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "foreign-workflow"), default);
        result.Error.Code.Should().Be("WorkflowRunNotFound");
        fixture.RunRows.Should().BeEmpty();
        fixture.VideoRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAsync_ForeignRun_DoesNotRevealCheckpointData()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct()); run.UserId = Guid.NewGuid();
        var result = await fixture.RunService.GetAsync(run.Id, default);
        result.Error.Code.Should().Be("WorkflowRunNotFound");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow("ai_background")]
    [DataRow("ai_shot")]
    public async Task ActionAsync_DisabledAiRerender_DoesNotBypassModeCapability(string mode)
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "rerender", Config: new(Mode: mode)), default);
        result.Error.Code.Should().Be("UnsupportedVideoMode");
        fixture.VideoRows.Should().HaveCount(1);
        fixture.JobRows.Should().BeEmpty();
        run.Status.Should().Be("waiting_for_review");
    }

    [TestMethod]
    public async Task StartAsync_ValidWorkflow_SnapshotsDefinitionAndPausesAtMockupApproval()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "first-run"), cancellation.Token);
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("waiting_for_input");
        var run = fixture.RunRows.Single();
        run.DefinitionSnapshot.Should().Be(workflow.Definition);
        run.WorkflowRevision.Should().Be(4);
        fixture.NodeRows.Should().HaveCount(6);
        fixture.NodeRows.Single(x => x.NodeType == "product-input").Status.Should().Be("succeeded");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
        fixture.VideoRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
        fixture.State.Verify(x => x.LockOwnerAsync(WorkflowTestData.Owner, cancellation.Token), Times.Once);
        fixture.Transaction.Verify(x => x.CommitAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_SameIdempotencyKey_ReturnsExistingRunWithoutDuplicatingVideoOrNodes()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var request = new StartRunRequest(4, "stable-request");
        var first = await fixture.RunService.StartAsync(workflow.Id, request, default);
        var repeated = await fixture.RunService.StartAsync(workflow.Id, request, default);
        repeated.Value.Id.Should().Be(first.Value.Id);
        fixture.RunRows.Should().HaveCount(1);
        fixture.NodeRows.Should().HaveCount(6);
        fixture.VideoRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_ReusedKeyForDifferentRevision_ReturnsConflict()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        await fixture.RunService.StartAsync(workflow.Id, new(4, "same-key"), default);
        var result = await fixture.RunService.StartAsync(workflow.Id, new(5, "same-key"), default);
        result.Error.Code.Should().Be("IdempotencyConflict");
        fixture.RunRows.Should().HaveCount(1);
    }

    [TestMethod]
    [DataRow("foreign_owner")]
    [DataRow("wrong_batch")]
    [DataRow("deleted")]
    public async Task StartAsync_UnavailableProduct_DoesNotCreateRun(string condition)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var workflow = fixture.AddWorkflow(product);
        if (condition == "foreign_owner") product.UserId = Guid.NewGuid();
        if (condition == "wrong_batch") product.BatchId = Guid.NewGuid();
        if (condition == "deleted") product.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "unauthorized"), default);
        result.Error.Code.Should().Be("ProductNotFound");
        fixture.RunRows.Should().BeEmpty();
        fixture.NodeRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("ai_background")]
    [DataRow("ai_shot")]
    public async Task StartAsync_ManualDisabledAiRequest_RejectsBeforeCreatingRun(string mode)
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct(), new(Mode: mode));
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "ai-request"), default);
        result.Error.Code.Should().Be("UnsupportedVideoMode");
        fixture.RunRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_NoApprovedMockups_CannotResumeOrRender()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct(), "waiting_for_input");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status = "waiting_for_input";
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "resume_mockups"), default);
        result.Error.Code.Should().Be("WaitingForInput");
        run.Status.Should().Be("waiting_for_input");
        fixture.VideoRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_ApprovedMockups_QueuesRenderAndKeepsReviewPending()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var run = fixture.AddRun(product, "waiting_for_input");
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "resume_mockups"), default);
        result.Value.Status.Should().Be("running");
        result.Value.Revision.Should().Be(8);
        var video = fixture.VideoRows.Single();
        video.Mode.Should().Be("standard");
        video.ApprovalStatus.Should().Be("pending");
        video.VersionNumber.Should().Be(1);
        fixture.SceneRows.Should().HaveCount(2).And.OnlyContain(x => x.MockupImageId == asset.Id && x.SourceRevision == 3);
        fixture.SceneRows.Select(x => x.SceneOrder).Should().Equal(1, 2);
        fixture.SceneRows.Select(x => WorkflowJson.Read<ScenePlan>(x.SceneConfig).SceneOrder).Should().Equal(0, 1);
        fixture.JobRows.Single().Kind.Should().Be("render");
        fixture.JobRows.Single().Status.Should().Be("queued");
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status.Should().Be("pending");
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("old_video")]
    [DataRow("old_review_revision")]
    [DataRow("old_run_revision")]
    public async Task ActionAsync_StaleExactApproval_DoesNotApproveCandidateOrExport(string condition)
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var candidate = fixture.VideoRows.Single();
        var id = condition == "old_video" ? Guid.NewGuid() : candidate.Id;
        var result = await fixture.RunService.ActionAsync(run.Id, new(condition == "old_run_revision" ? 6 : 7,
            "approve_video", id, condition == "old_review_revision" ? 1 : 2), default);
        result.Error.Code.Should().Be("StaleRevision");
        candidate.ApprovalStatus.Should().Be("pending");
        fixture.ExportRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_ApproveExactRevision_CreatesExportFromOnlyApprovedVideo()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", video.Id, 2), default);
        result.Value.Status.Should().Be("running");
        video.ApprovalStatus.Should().Be("approved");
        video.ApprovedBy.Should().Be(WorkflowTestData.Owner);
        video.ApprovedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        video.ReviewRevision.Should().Be(3);
        var export = fixture.ExportRows.Single();
        export.Status.Should().Be("preparing");
        export.Manifest.Should().Contain(video.Id.ToString()).And.Contain("standard").And.NotContain("https:").And.NotContain("prompt");
        fixture.ItemRows.Single().PromoVideoId.Should().Be(video.Id);
        fixture.JobRows.Single().Kind.Should().Be("export_zip");
    }

    [TestMethod]
    public async Task ActionAsync_RejectVideo_KeepsReviewCheckpointWithoutExport()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "reject_video", video.Id, 2), default);
        result.Value.Status.Should().Be("waiting_for_review");
        video.ApprovalStatus.Should().Be("rejected");
        video.ApprovedBy.Should().BeNull();
        video.ReviewRevision.Should().Be(3);
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_Rerender_CreatesNewVersionAndPreservesOldArtifact()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var old = fixture.VideoRows.Single();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "rerender", Config: new(TextOverlay: "Version two")), default);
        result.Value.Status.Should().Be("running");
        fixture.VideoRows.Should().HaveCount(2);
        var candidate = fixture.VideoRows.Single(x => x.Id != old.Id);
        candidate.SeriesId.Should().Be(old.SeriesId);
        candidate.VersionNumber.Should().Be(2);
        candidate.ApprovalStatus.Should().Be("pending");
        candidate.ConfigSnapshot.Should().Contain("Version two");
        old.StorageKey.Should().Be("private/approved-video");
        old.Status.Should().Be("completed");
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Attempt.Should().Be(2);
    }

    [TestMethod]
    public async Task ActionAsync_RerenderWithStaleStoryboardFingerprint_ReturnsConflictWithoutChangingCandidate()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        var originalInput = generate.InputSnapshot;

        var result = await fixture.RunService.ActionAsync(run.Id,
            new(7, "rerender", Config: new(TemplateVersion: 2), ExpectedStoryboardFingerprint: "stale"), default);

        result.Error.Code.Should().Be("StoryboardChanged");
        fixture.VideoRows.Should().ContainSingle();
        fixture.JobRows.Should().BeEmpty();
        generate.Attempt.Should().Be(1);
        generate.InputSnapshot.Should().Be(originalInput);
    }

    [TestMethod]
    public async Task ActionAsync_RerenderWithMatchingStoryboardFingerprint_QueuesExactPreview()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var config = new GenerateVideoConfig(TemplateVersion: 2);
        var fingerprint = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, config).Value.Fingerprint;

        var result = await fixture.RunService.ActionAsync(run.Id,
            new(7, "rerender", Config: config, ExpectedStoryboardFingerprint: fingerprint), default);

        result.Value.Status.Should().Be("running");
        fixture.VideoRows.Should().HaveCount(2);
        fixture.VideoRows.MaxBy(x => x.VersionNumber)!.Fingerprint.Should().Be(fingerprint);
    }

    [TestMethod]
    public async Task ActionAsync_RetryFailedSameFingerprint_ReusesLogicalVideoAndScenes()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture); run.Status = "failed";
        var existing = fixture.VideoRows.Single(); existing.Status = "failed";
        existing.Fingerprint = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, new()).Value.Fingerprint;
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video"); generate.Status = "failed";
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "retry"), default);
        result.Value.Status.Should().Be("running");
        fixture.VideoRows.Should().HaveCount(1);
        fixture.SceneRows.Should().HaveCount(1);
        existing.Status.Should().Be("queued");
        existing.VersionNumber.Should().Be(1);
        generate.Attempt.Should().Be(1);
        WorkflowJson.Read<RenderJobPayload>(fixture.JobRows.Single().Payload).VideoId.Should().Be(existing.Id);
    }

    [TestMethod]
    public async Task GetAsync_StoredRun_ReadsSnapshotAndCheckpointState()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var started = await fixture.RunService.StartAsync(workflow.Id, new(4, "durable"), default);
        var result = await fixture.RunService.GetAsync(started.Value.Id, default);
        result.Value.Id.Should().Be(started.Value.Id);
        result.Value.Status.Should().Be("waiting_for_input");
        result.Value.Nodes.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
        fixture.RunRows.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task ActionAsync_Cancel_RevokesLeaseAndPreservesCompletedCheckpoints()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var input = fixture.NodeRows.Single(x => x.NodeType == "product-input"); input.Status = "succeeded";
        var job = fixture.AddJob(run);
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "cancel"), default);
        result.Value.Status.Should().Be("cancelled");
        run.CompletedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        job.Status.Should().Be("cancelled");
        job.LeaseToken.Should().BeNull();
        input.Status.Should().Be("succeeded");
        fixture.NodeRows.Where(x => x != input).Should().OnlyContain(x => x.Status == "cancelled");
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_ForeignRun_DoesNotCancelOrRevealState()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct()); run.UserId = Guid.NewGuid();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "cancel"), default);
        result.Error.Code.Should().Be("WorkflowRunNotFound");
        run.Status.Should().Be("running");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    internal static WorkflowRun PrepareReview(WorkflowTestData fixture)
    {
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var run = fixture.AddRun(product, "waiting_for_review");
        foreach (var node in fixture.NodeRows.Where(x => x.NodeType is not ("review-video" or "export-zip"))) node.Status = "succeeded";
        var video = new PromoVideo { Id = Guid.NewGuid(), ProductId = product.Id, WorkflowRunId = run.Id, Status = "completed", Mode = "standard",
            ApprovalStatus = "pending", ReviewRevision = 2, VersionNumber = 1, SeriesId = Guid.NewGuid(), StorageKey = "private/approved-video", StorageVersion = "1",
            ThumbnailStorageKey = "private/thumbnail", ThumbnailStorageVersion = "1", Fingerprint = "fixed-fingerprint" };
        fixture.VideoRows.Add(video);
        var review = fixture.NodeRows.Single(x => x.NodeType == "review-video"); review.Status = "waiting_for_review"; review.OutputSnapshot = WorkflowJson.Write(new VideoPointer(video.Id));
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").OutputSnapshot = review.OutputSnapshot;
        fixture.SceneRows.Add(new() { Id = Guid.NewGuid(), PromoVideoId = video.Id, MockupImageId = asset.Id, SourceRevision = 3,
            SourceHash = asset.ContentHash, GenerationStrategy = "standard", SceneOrder = 0 });
        return run;
    }
}
