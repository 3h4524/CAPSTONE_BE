using System.Text.Json;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowExportBarrierTests
{
    /// <summary>The run past Review Video, as Continue leaves it: its video decided on, the export still to start.</summary>
    private static void PassReview(WorkflowTestData fixture, WorkflowRun run, string decision = "approved")
    {
        fixture.VideoRows.Single(x => x.WorkflowRunId == run.Id).ApprovalStatus = decision;
        fixture.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "review-video").Status = "succeeded";
        run.Status = "running";
    }

    [TestMethod]
    [DataRow("running")]
    [DataRow("waiting_for_review")]
    [DataRow("failed")]
    public async Task Advance_UnfinishedSibling_DoesNotQueueExport(string siblingStatus)
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        var sibling = fixture.AddRun(fixture.AddProduct(), siblingStatus);
        run.RunGroupId = sibling.RunGroupId = Guid.NewGuid();
        var siblingSteps = fixture.NodeRows.Where(x => x.WorkflowRunId == sibling.Id).ToArray();
        siblingSteps.Single(x => x.NodeType == "generate-video").Status = siblingStatus == "waiting_for_review" ? "succeeded" : siblingStatus;
        siblingSteps.Single(x => x.NodeType == "review-video").Status = siblingStatus == "waiting_for_review" ? siblingStatus : "pending";
        PassReview(fixture, run);

        await fixture.Runtime.AdvanceAsync(run, default);

        fixture.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "export-zip").Status.Should().Be("pending");
        fixture.JobRows.Should().BeEmpty();
        fixture.ExportRows.Should().BeEmpty();
        run.Status.Should().Be("running");
    }

    [TestMethod]
    [DataRow("approve_video", "approved")]
    [DataRow("reject_video", "rejected")]
    public async Task Decision_InABatch_IsOnlyRecordedUntilContinue(string action, string approvalStatus)
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        run.RunGroupId = Guid.NewGuid();
        var video = fixture.VideoRows.Single();

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, action, video.Id, 2), default);

        result.IsSuccess.Should().BeTrue();
        video.ApprovalStatus.Should().Be(approvalStatus);
        run.Status.Should().Be("waiting_for_review");
        fixture.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "review-video").Status.Should().Be("waiting_for_review");
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Decision_InABatch_CanBeChangedBeforeContinue()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        run.RunGroupId = Guid.NewGuid();
        var video = fixture.VideoRows.Single();
        await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", video.Id, 2), default);

        var result = await fixture.RunService.ActionAsync(run.Id, new(run.Revision, "reject_video", video.Id, video.ReviewRevision), default);

        result.IsSuccess.Should().BeTrue();
        video.ApprovalStatus.Should().Be("rejected");
        video.ApprovedBy.Should().BeNull();
    }

    [TestMethod]
    public async Task Advance_RejectedVideo_ExportsTheProductWithoutIt()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        PassReview(fixture, run, "rejected");

        await fixture.Runtime.AdvanceAsync(run, default);

        var job = fixture.JobRows.Should().ContainSingle().Which;
        job.Kind.Should().Be("export_zip");
        var payload = WorkflowJson.Read<ExportJobPayload>(job.Payload);
        payload.VideoId.Should().BeNull();
        payload.VideoStorageKey.Should().BeNull();
        payload.Storyboard.Should().BeNull();
        payload.EtsyGuide.Should().Contain("No video: it was rejected at review.");
        payload.Manifest.GetProperty("videoId").ValueKind.Should().Be(JsonValueKind.Null);
        fixture.ItemRows.Single().Should().BeEquivalentTo(new { PromoVideoId = (Guid?)null, IncludePromoVideo = (bool?)false });
        fixture.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "export-zip").Status.Should().Be("running");
    }

    [TestMethod]
    public async Task Advance_ApprovedVideo_ExportsItWithTheProduct()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        PassReview(fixture, run);

        await fixture.Runtime.AdvanceAsync(run, default);

        var payload = WorkflowJson.Read<ExportJobPayload>(fixture.JobRows.Single().Payload);
        payload.VideoId.Should().Be(fixture.VideoRows.Single().Id);
        payload.VideoStorageKey.Should().Be("private/approved-video");
        fixture.ItemRows.Single().IncludePromoVideo.Should().BeTrue();
    }

    [TestMethod]
    public async Task AdvanceNextAsync_AllVideosApproved_ResumesWaitingExportOnlyOnce()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        var sibling = fixture.AddRun(fixture.AddProduct(), "running");
        run.RunGroupId = sibling.RunGroupId = Guid.NewGuid();
        PassReview(fixture, run);
        await fixture.Runtime.AdvanceAsync(run, default);
        fixture.JobRows.Should().BeEmpty();
        foreach (var step in fixture.NodeRows.Where(x => x.WorkflowRunId == sibling.Id && x.NodeType is "generate-video" or "review-video"))
            step.Status = "succeeded";
        fixture.State.SetupSequence(x => x.ClaimQueuedGroupRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(run).ReturnsAsync((WorkflowRun?)null);
        var planner = new RunGroupPlanner(fixture.State.Object, fixture.Unit.Object, fixture.Runtime, fixture.Time);

        (await planner.AdvanceNextAsync(_ => { }, default)).Should().BeTrue();
        await fixture.Runtime.AdvanceAsync(run, default);
        (await planner.AdvanceNextAsync(_ => { }, default)).Should().BeFalse();

        fixture.JobRows.Should().ContainSingle().Which.Kind.Should().Be("export_zip");
        fixture.ExportRows.Should().ContainSingle();
        fixture.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "export-zip")
            .Should().BeEquivalentTo(new { Status = "running", Stage = "Exporting", Progress = 0 });
    }
}
