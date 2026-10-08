using APCS.Application.Features.Workflows;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowRuntimeTests
{
    [TestMethod]
    public async Task AdvanceAsync_NewRun_PausesBeforeMediaGenerationAtMockupGate()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        await fixture.Runtime.AdvanceAsync(run, default);
        run.Status.Should().Be("waiting_for_input");
        fixture.NodeRows.Take(2).Should().OnlyContain(x => x.Status == "succeeded");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("running")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    public async Task AdvanceAsync_NodeAlreadyActiveOrStopped_DoesNotAdvanceOrQueueJob(string status)
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        fixture.NodeRows[0].Status = status;
        await fixture.Runtime.AdvanceAsync(run, default);
        fixture.NodeRows[0].Status.Should().Be(status);
        fixture.NodeRows.Skip(1).Should().OnlyContain(x => x.Status == "pending");
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AdvanceAsync_MissingValidSource_ReturnsToWaitingForInput()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        foreach (var node in fixture.NodeRows.Take(3)) node.Status = "succeeded";
        await fixture.Runtime.AdvanceAsync(run, default);
        run.Status.Should().Be("waiting_for_input");
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        generate.Status.Should().Be("waiting_for_input");
        generate.ErrorMessage.Should().Contain("Approve at least one mockup");
        fixture.VideoRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ApprovedAssetsAsync_SelectionArtworkAndApproval_ExcludesAllIneligibleSources()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var run = fixture.AddRun(product);
        var hero = fixture.AddAsset(product);
        var otherGroup = fixture.AddAsset(product); otherGroup.ArtworkGroupKey = "other";
        var stale = fixture.AddAsset(product); stale.MetadataRevision++;
        var unselected = fixture.AddAsset(product);
        var deleted = fixture.AddAsset(product); deleted.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        fixture.AddAsset(fixture.AddProduct());
        var selected = await fixture.Runtime.ApprovedAssetsAsync(run, new([hero.Id, otherGroup.Id, stale.Id, deleted.Id], "artwork-main"), default);
        selected.Select(x => x.Id).Should().Equal(hero.Id);
        selected.Should().NotContain(unselected);
    }

    [TestMethod]
    public async Task AdvanceAsync_ExportWithoutExactApproval_FailsWithoutPackage()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture); run.Status = "running";
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status = "succeeded";
        await fixture.Runtime.AdvanceAsync(run, default);
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Be("Export requires the exact approved video.");
        fixture.NodeRows.Single(x => x.NodeType == "export-zip").Status.Should().Be("failed");
        fixture.ExportRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AdvanceAsync_AllCheckpointsSucceeded_CompletesAtControlledTime()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        foreach (var node in fixture.NodeRows) node.Status = "succeeded";
        await fixture.Runtime.AdvanceAsync(run, default);
        run.Status.Should().Be("completed");
        run.CompletedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        fixture.JobRows.Should().BeEmpty();
    }
}
