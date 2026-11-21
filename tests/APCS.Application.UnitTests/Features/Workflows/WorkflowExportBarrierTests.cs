using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowExportBarrierTests
{
    [TestMethod]
    [DataRow("running")]
    [DataRow("waiting_for_review")]
    [DataRow("failed")]
    public async Task ApproveVideo_UnfinishedSibling_DoesNotQueueExport(string siblingStatus)
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        var sibling = fixture.AddRun(fixture.AddProduct(), siblingStatus);
        run.RunGroupId = sibling.RunGroupId = Guid.NewGuid();
        var siblingSteps = fixture.NodeRows.Where(x => x.WorkflowRunId == sibling.Id).ToArray();
        siblingSteps.Single(x => x.NodeType == "generate-video").Status = siblingStatus == "waiting_for_review" ? "succeeded" : siblingStatus;
        siblingSteps.Single(x => x.NodeType == "review-video").Status = siblingStatus == "waiting_for_review" ? siblingStatus : "pending";

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", fixture.VideoRows.Single().Id, 2), default);

        result.IsSuccess.Should().BeTrue();
        fixture.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "review-video").Status.Should().Be("succeeded");
        fixture.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "export-zip").Status.Should().Be("pending");
        fixture.JobRows.Should().BeEmpty();
        fixture.ExportRows.Should().BeEmpty();
        run.Status.Should().Be("running");
    }

    [TestMethod]
    public async Task AdvanceNextAsync_AllVideosApproved_ResumesWaitingExportOnlyOnce()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        var sibling = fixture.AddRun(fixture.AddProduct(), "running");
        run.RunGroupId = sibling.RunGroupId = Guid.NewGuid();
        await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", fixture.VideoRows.Single().Id, 2), default);
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
