using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
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
    public async Task ApprovedAssetsAsync_ArtworkGroupOfAnotherProduct_KeepsTheApprovedMockups()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var run = fixture.AddRun(product);
        var approved = fixture.AddAsset(product);
        var pending = fixture.AddAsset(product); pending.ApprovalStatus = "pending"; pending.ApprovedRevision = null;
        var selected = await fixture.Runtime.ApprovedAssetsAsync(run, new([], "group-saved-for-an-earlier-product"), default);
        selected.Select(x => x.Id).Should().Equal(approved.Id);
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
    public async Task AdvanceAsync_ListingFailsInAVideoRun_LeavesItOnItsStepAndKeepsTheVideoGoing()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var listing = fixture.AddListingStep(run);
        fixture.ListingFails("Gemini is overloaded right now.");

        await fixture.Runtime.AdvanceAsync(run, default);

        listing.Status.Should().Be("failed");
        listing.ErrorMessage.Should().Be("Gemini is overloaded right now.");
        // The video is made from the mock-ups, so it still reaches Mockup Approval.
        run.Status.Should().Be("waiting_for_input");
        run.ErrorMessage.Should().BeNull();
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
    }

    [TestMethod]
    public async Task AdvanceAsync_ListingFailsInAListingOnlyRun_FailsTheRun()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddListingOnlyRun(fixture.AddProduct());
        fixture.ListingFails("Gemini returned an incomplete listing.");

        await fixture.Runtime.AdvanceAsync(run, default);

        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Be("Gemini returned an incomplete listing.");
        fixture.NodeRows.Single(x => x.NodeType == "generate-listing").Status.Should().Be("failed");
    }

    [TestMethod]
    public async Task AdvanceAsync_ListingAlreadyFailed_IsNotWrittenAgainByTheVideoSteps()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        fixture.AddListingStep(run, "failed");
        fixture.ListingSucceeds();

        await fixture.Runtime.AdvanceAsync(run, default);

        fixture.NodeRows.Single(x => x.NodeType == "generate-listing").Status.Should().Be("failed");
        fixture.ListingContent.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task RetryListingAsync_VideoRunWaitingAtTheGate_WritesTheListingAndTouchesNothingElse()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct(), "waiting_for_input");
        foreach (var node in fixture.NodeRows.Take(2)) node.Status = "succeeded";
        var gate = fixture.NodeRows.Single(x => x.NodeType == "approval-gate"); gate.Status = "waiting_for_input";
        var listing = fixture.AddListingStep(run, "failed"); listing.ErrorMessage = "Gemini is overloaded right now.";
        fixture.ListingSucceeds();

        await fixture.Runtime.RetryListingAsync(run, fixture.NodeRows, default);

        listing.Status.Should().Be("succeeded");
        listing.Attempt.Should().Be(2);
        listing.ErrorMessage.Should().BeNull();
        listing.OutputSnapshot.Should().Contain("Vintage road trip tee");
        run.Status.Should().Be("waiting_for_input");
        gate.Status.Should().Be("waiting_for_input");
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Status.Should().Be("pending");
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(true, "completed")]
    [DataRow(false, "failed")]
    public async Task RetryListingAsync_ListingOnlyRun_EndsTheRunWithTheListing(bool succeeds, string expected)
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddListingOnlyRun(fixture.AddProduct(), "failed");
        run.ErrorMessage = "Gemini is overloaded right now.";
        fixture.NodeRows.Single(x => x.NodeType == "generate-listing").Status = "failed";
        if (succeeds) fixture.ListingSucceeds(); else fixture.ListingFails("Still overloaded.");

        await fixture.Runtime.RetryListingAsync(run, fixture.NodeRows, default);

        run.Status.Should().Be(expected);
        run.ErrorMessage.Should().Be(succeeds ? null : "Still overloaded.");
    }

    [TestMethod]
    [DataRow("failed", "pending", false)]
    [DataRow("submitting", "submitting", true)]
    [DataRow("stored", "stored", true)]
    public async Task AdvanceAsync_RetryOfAnAiVideo_SubmitsOnlyTheSceneGoogleAnsweredWithoutAClip(string before, string after, bool keepsOperation)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        fixture.AddAsset(product);
        var run = fixture.AddRun(product);
        foreach (var node in fixture.NodeRows.Take(3)) node.Status = "succeeded";
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 8, AiShotOptions: new(Coverage: "full_video"));
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        generate.InputSnapshot = WorkflowJson.Write(config);
        var shot = new VideoStoryboardPlanner().Plan(product, fixture.AssetRows, config).Value.AiShots!.Single();
        // The job of the attempt that failed, with what it knew about the scene.
        fixture.JobRows.Add(new MediaJob { Id = Guid.NewGuid(), WorkflowRunId = run.Id, WorkflowNodeRunId = generate.Id, UserId = WorkflowTestData.Owner,
            Kind = "render", Status = "failed", MaximumAttempts = 3,
            Payload = WorkflowJson.Write(new AiRenderJobPayload(Guid.NewGuid(), new VideoStoryboardPlanner().Plan(product, fixture.AssetRows, config).Value,
                [new AiShotJobState(shot.SceneOrder, shot.Fingerprint, "models/veo/operations/old", Status: before, Failure: "Audio issue.")])) });

        await fixture.Runtime.AdvanceAsync(run, default);

        var state = WorkflowJson.Read<AiRenderJobPayload>(fixture.JobRows.Single().Payload).AiShotStates.Single();
        state.Status.Should().Be(after);
        state.OperationName.Should().Be(keepsOperation ? "models/veo/operations/old" : null);
        fixture.JobRows.Single().Status.Should().Be("queued");
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
