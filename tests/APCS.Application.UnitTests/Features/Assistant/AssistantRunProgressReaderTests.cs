using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant;

[TestClass]
public sealed class AssistantRunProgressReaderTests
{
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid BatchId = Guid.NewGuid();
    private static readonly DateTimeOffset Earlier = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddHours(1);

    private readonly Mock<IWorkflowService> workflows = new();
    private readonly Mock<IWorkflowRunService> runs = new();
    private readonly Mock<IWorkflowRunGroupService> groups = new();
    private readonly Mock<IDesignGenerationService> jobs = new();
    private readonly Mock<IMockupTemplateService> jobMockups = new();

    public AssistantRunProgressReaderTests()
    {
        var input = new WorkflowNode("a", "product-input", "Input", new Point(), AssistantTestSupport.Json($$"""{"batchId":"{{BatchId}}"}"""));
        var definition = new WorkflowDefinition(2, [input], []);
        workflows.Setup(service => service.GetAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowResponse(WorkflowId, "Etsy flow", "", 1, 3, Earlier, Earlier, definition)));
        WithJobs();
        WithGroups();
        WithJobMockups(0);
        runs.Setup(service => service.GetLatestAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RunResponse>(Error.NotFound("RunNotFound", "No run.")));
    }

    private AssistantRunProgressReader Reader() => new(workflows.Object, runs.Object, groups.Object, jobs.Object, jobMockups.Object);

    private void WithJobMockups(int count) =>
        jobMockups.Setup(service => service.GetJobMockupsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new GenerateAllMockupsResultDto(count, 0, 0, [],
                Enumerable.Range(0, count).Select(_ => new MockupImageResponseDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "u", 1, 1, "pending", null)).ToArray())));

    private void WithJobs(params BatchJobSummaryDto[] items) =>
        jobs.Setup(service => service.ListJobsForBatchAsync(BatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<BatchJobSummaryDto>>(items));

    private void WithGroups(params RunGroupSummaryResponse[] items) =>
        groups.Setup(service => service.ListAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<RunGroupSummaryResponse>>(items));

    private static BatchJobSummaryDto Job(string status, DateTimeOffset at, Guid? workflowId = null) =>
        new(Guid.NewGuid(), status, 4, 2, 0, at.UtcDateTime, at.UtcDateTime, workflowId ?? WorkflowId);

    private static BatchJobDetailDto JobDetail(Guid id, params string[] productStatuses) => new(
        id, BatchId, "Batch", "completed", productStatuses.Length, productStatuses.Length, 0, 100, null, null, 2, "1:1",
        new BatchJobCountersDto(0, 0, productStatuses.Length, 0),
        productStatuses.Select((status, index) => new BatchJobProductResultDto(Guid.NewGuid(), Guid.NewGuid(), $"P{index}", "tshirt", index, status, null, [])).ToArray(),
        RequireApproval: true, WorkflowId: WorkflowId);

    private static RunGroupTargetResponse Target(int pending = 0, string? runStatus = null, string? error = null) => new(
        "k", Guid.NewGuid(), "Shirt", "tshirt", "g", "Design 1", null, 3, 3 - pending, pending, true, false, [], [], null,
        null, runStatus, null, null, 0, error);

    private void WithGroup(Guid id, string status, RunGroupProgress progress, params RunGroupTargetResponse[] targets) =>
        WithGroupDetail(new RunGroupResponse(id, WorkflowId, BatchId, null, status, 2, 3, Later, 10, targets.Length, progress, targets));

    private void WithGroupDetail(RunGroupResponse group) =>
        groups.Setup(service => service.GetAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(group));

    private static RunResponse Run(string status, DateTimeOffset at, Guid? groupId = null, params NodeRunResponse[] nodes) =>
        new(Guid.NewGuid(), WorkflowId, Guid.NewGuid(), status, 1, 3, at, null, nodes, groupId);

    private static NodeRunResponse Node(string type, string status, string? error = null) =>
        new(Guid.NewGuid(), type, type, status, 1, null, 0, error, AssistantTestSupport.Json("{}"));

    [TestMethod]
    public async Task ReadAsync_WithoutAnyRun_SaysNotStarted()
    {
        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.NotStarted);
        progress.State.Should().Be(AssistantRunStates.Idle);
        progress.WorkflowName.Should().Be("Etsy flow");
    }

    [TestMethod]
    public async Task ReadAsync_WhenTheWorkflowIsMissing_ReturnsItsError()
    {
        var missingId = Guid.NewGuid();
        workflows.Setup(service => service.GetAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<WorkflowResponse>(Error.NotFound("WorkflowNotFound", "Workflow not found.")));

        var progress = await Reader().ReadAsync(missingId, default);

        progress.IsFailure.Should().BeTrue();
        progress.Error.Code.Should().Be("WorkflowNotFound");
    }

    [TestMethod]
    public async Task ReadAsync_WithARunningDesignJob_ReportsDesignsWorking()
    {
        WithJobs(Job("running", Later), Job("running", Later.AddHours(1), Guid.NewGuid()));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.Designs);
        progress.State.Should().Be(AssistantRunStates.Working);
        progress.Done.Should().Be(2);
        progress.Total.Should().Be(4);
    }

    [TestMethod]
    public async Task ReadAsync_WithDesignsWaitingForReview_AsksForDesignApproval()
    {
        var job = Job("completed", Later);
        WithJobs(job, Job("draft", Later.AddHours(2)));
        jobs.Setup(service => service.GetJobAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(JobDetail(job.Id, "image_review_required", "image_review_required", "approved")));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.DesignApproval);
        progress.State.Should().Be(AssistantRunStates.WaitingForUser);
        progress.WaitingCount.Should().Be(2);
        progress.BatchJobId.Should().Be(job.Id);
    }

    [TestMethod]
    public async Task ReadAsync_WithReviewedDesignsAndNoMockups_SaysTheyAreReadyForTheNextStep()
    {
        var job = Job("completed", Later);
        WithJobs(job);
        jobs.Setup(service => service.GetJobAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(JobDetail(job.Id, "approved", "approved")));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.DesignsReady);
        progress.State.Should().Be(AssistantRunStates.WaitingForUser);
    }

    [TestMethod]
    public async Task ReadAsync_WithMockupsMadeForAWorkflowWithoutVideo_SaysTheRunIsDone()
    {
        var job = Job("completed", Later);
        WithJobs(job);
        WithJobMockups(3);
        jobs.Setup(service => service.GetJobAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(JobDetail(job.Id, "approved", "approved")));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.Done);
        progress.State.Should().Be(AssistantRunStates.Done);
        progress.NextStep.Should().Contain("3 mock-ups are ready");
    }

    [TestMethod]
    public async Task ReadAsync_WhenAVideoGroupIsNewerThanTheJob_AsksForMockupApproval()
    {
        var groupId = Guid.NewGuid();
        WithJobs(Job("completed", Earlier));
        WithGroups(new RunGroupSummaryResponse(groupId, "waiting_for_targets", Later, 2, 0));
        WithGroup(groupId, "waiting_for_targets", new RunGroupProgress(0, 0, 0, 0, 0, 0, 0, 0), Target(pending: 2), Target(pending: 1));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.MockupApproval);
        progress.State.Should().Be(AssistantRunStates.WaitingForUser);
        progress.WaitingCount.Should().Be(3);
        progress.RunGroupId.Should().Be(groupId);
    }

    [TestMethod]
    public async Task ReadAsync_WhileAGroupIsPreparedInTheBackground_SaysThereIsNothingToDoYet()
    {
        var groupId = Guid.NewGuid();
        WithGroups(new RunGroupSummaryResponse(groupId, "waiting_for_targets", Later, 1, 0));
        WithGroupDetail(new RunGroupResponse(groupId, WorkflowId, BatchId, null, "waiting_for_targets", 2, 3, Later, 10, 1,
            new RunGroupProgress(0, 0, 0, 0, 0, 0, 0, 0), [Target(pending: 2)], Preparing: true));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.MockupApproval);
        progress.State.Should().Be(AssistantRunStates.Working);
    }

    [TestMethod]
    public async Task ReadAsync_AtMockupApproval_PassesOnTheGroupNotice()
    {
        var groupId = Guid.NewGuid();
        WithGroups(new RunGroupSummaryResponse(groupId, "waiting_for_targets", Later, 1, 0));
        WithGroupDetail(new RunGroupResponse(groupId, WorkflowId, BatchId, null, "waiting_for_targets", 2, 3, Later, 10, 1,
            new RunGroupProgress(0, 0, 0, 0, 0, 0, 0, 0), [Target(pending: 1)], Notice: "The AI clips cost more than the budget."));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.State.Should().Be(AssistantRunStates.WaitingForUser);
        progress.NextStep.Should().Contain("The AI clips cost more than the budget.");
    }

    [TestMethod]
    public async Task ReadAsync_WithMockupsMadeForAWorkflowThatPublishesWithoutVideo_SaysTheNextPartIsReady()
    {
        var input = new WorkflowNode("a", "product-input", "Input", new Point(), AssistantTestSupport.Json($$"""{"batchId":"{{BatchId}}"}"""));
        var publish = new WorkflowNode("p", "publish-printify", "Push to Printify", new Point(), AssistantTestSupport.Json("{}"));
        workflows.Setup(service => service.GetAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkflowResponse(WorkflowId, "Etsy flow", "", 2, 3, Earlier, Earlier, new WorkflowDefinition(2, [input, publish], []))));
        var job = Job("completed", Later);
        WithJobs(job);
        WithJobMockups(2);
        jobs.Setup(service => service.GetJobAsync(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(JobDetail(job.Id, "approved")));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.MockupsReady);
        progress.State.Should().Be(AssistantRunStates.WaitingForUser);
    }

    [TestMethod]
    public async Task ReadAsync_WithVideosWaitingForReview_AsksForVideoReview()
    {
        var groupId = Guid.NewGuid();
        WithGroups(new RunGroupSummaryResponse(groupId, "running", Later, 2, 1));
        WithGroup(groupId, "running", new RunGroupProgress(2, 0, 0, 1, 1, 0, 0, 0), Target(runStatus: "waiting_for_review"), Target(runStatus: "completed"));
        runs.Setup(service => service.GetLatestAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Run("waiting_for_review", Later.AddMinutes(5), groupId)));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.VideoReview);
        progress.WaitingCount.Should().Be(1);
        progress.Targets.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task ReadAsync_WithAFinishedGroupWithFailures_ReportsTheFailure()
    {
        var groupId = Guid.NewGuid();
        WithGroups(new RunGroupSummaryResponse(groupId, "completed", Later, 2, 1));
        WithGroup(groupId, "completed", new RunGroupProgress(2, 0, 0, 0, 1, 1, 0, 0), Target(runStatus: "failed", error: "Render failed"), Target(runStatus: "completed"));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.State.Should().Be(AssistantRunStates.Failed);
        progress.Error.Should().Be("Render failed");
    }

    [TestMethod]
    public async Task ReadAsync_WithAFailedSingleRun_ReportsTheFailedStep()
    {
        runs.Setup(service => service.GetLatestAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Run("failed", Later, null,
                Node("product-input", "succeeded"), Node("export-zip", "failed", "Disk full"))));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.Export);
        progress.State.Should().Be(AssistantRunStates.Failed);
        progress.Error.Should().Be("Disk full");
        progress.Done.Should().Be(1);
    }

    [TestMethod]
    public async Task ReadAsync_WithASingleRunAtTheApprovalGate_AsksForMockupApproval()
    {
        runs.Setup(service => service.GetLatestAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Run("waiting_for_input", Later, null,
                Node("design-image", "pending"), Node("product-input", "succeeded"), Node("approval-gate", "waiting_for_input"))));

        var progress = (await Reader().ReadAsync(WorkflowId, default)).Value;

        progress.Stage.Should().Be(AssistantRunStages.MockupApproval);
        progress.State.Should().Be(AssistantRunStates.WaitingForUser);
    }
}
