using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.Confirmations;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class WorkflowRunActionToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IWorkflowService> workflows = new();
    private readonly Mock<IWorkflowRunService> runs = new();
    private readonly Mock<IVideoArtifactService> videos = new();
    private readonly Mock<IAssistantPendingActionRepository> pendingActions = new();
    private readonly List<AssistantPendingAction> added = [];
    private readonly Guid runId = Guid.NewGuid();
    private readonly Guid workflowId = Guid.NewGuid();

    public WorkflowRunActionToolTests()
    {
        pendingActions.Setup(r => r.AddAsync(It.IsAny<AssistantPendingAction>(), true, It.IsAny<CancellationToken>()))
            .Callback<AssistantPendingAction, bool, CancellationToken>((action, _, _) => added.Add(action)).Returns(Task.CompletedTask);
        runs.Setup(r => r.GetAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RunResponse(runId, workflowId, Guid.NewGuid(), "waiting_for_review", 4, 2, Now, null, [])));
        UseMode("standard");
    }

    private WorkflowRunActionTool Tool() =>
        new(workflows.Object, runs.Object, videos.Object, pendingActions.Object, new FakeTimeProvider(Now));

    private void UseMode(string mode)
    {
        var node = new WorkflowNode("v", "generate-video", "Video", new Point(), AssistantTestSupport.Json($$"""{"mode":"{{mode}}"}"""));
        workflows.Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(
            new WorkflowResponse(workflowId, "Flow", "", 1, 2, Now, Now, new WorkflowDefinition(2, [node], []))));
    }

    private static VideoResponse Video(Guid id, int version, long reviewRevision) =>
        new(id, Guid.NewGuid(), "standard", "auto", version, reviewRevision, "ready", "pending", null, null, default, []);

    private static AssistantToolContext Context(Guid? contextRun = null) => new(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Seller,
        new AssistantClientContextDto("/workflows", null, contextRun, null, null));

    private Task<AssistantToolOutcome> Execute(string action, string? extra = null, Guid? contextRun = null) =>
        Tool().ExecuteAsync(Context(contextRun), AssistantTestSupport.Json($$"""{"runId":"{{runId}}","action":"{{action}}"{{extra}}}"""), default);

    [TestMethod]
    [DataRow("resume_mockups")]
    [DataRow("retry")]
    public async Task ExecuteAsync_PaidAiShotAction_RequiresUi(string action)
    {
        UseMode("ai_shot");

        var outcome = await Execute(action);

        outcome.ModelResultJson.Should().Contain("paid_action_requires_ui");
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ExecuteAsync_CancelOnAiShotWorkflow_IsAllowed()
    {
        UseMode("ai_shot");

        var outcome = await Execute("cancel");

        outcome.Succeeded.Should().BeTrue();
        added.Single().Title.Should().Be("Cancel run");
    }

    [TestMethod]
    public async Task ExecuteAsync_UnsupportedAction_IsRejected()
    {
        var outcome = await Execute("delete_everything");

        outcome.Succeeded.Should().BeFalse();
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ExecuteAsync_ApproveWithoutVideos_ReturnsNoVideo()
    {
        videos.Setup(v => v.ListAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<VideoResponse>>([]));

        var outcome = await Execute("approve_video");

        outcome.ModelResultJson.Should().Contain("no_video");
    }

    [TestMethod]
    public async Task ExecuteAsync_ApproveWithoutVideoId_PicksNewestVersionAndStoresReviewRevision()
    {
        var newest = Guid.NewGuid();
        videos.Setup(v => v.ListAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<VideoResponse>>(
            [Video(Guid.NewGuid(), 1, 3), Video(newest, 2, 9)]));

        var outcome = await Execute("approve_video");

        outcome.Succeeded.Should().BeTrue();
        var action = added.Single();
        action.Title.Should().Be("Approve video v2");
        action.Arguments.Should().Contain(newest.ToString()).And.Contain("\"reviewRevision\":9").And.Contain("\"expectedRevision\":4");
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutRunId_UsesRunFromContext()
    {
        var outcome = await Tool().ExecuteAsync(Context(runId), AssistantTestSupport.Json("""{"action":"cancel"}"""), default);

        outcome.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRunNotOwned_ReturnsError()
    {
        runs.Setup(r => r.GetAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure<RunResponse>(Error.NotFound("Missing", "Run not found.")));

        var outcome = await Execute("cancel");

        outcome.Succeeded.Should().BeFalse();
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ExecuteConfirmedAsync_ForwardsStoredRevisionVideoAndReviewRevision()
    {
        var videoId = Guid.NewGuid();
        RunActionRequest? captured = null;
        runs.Setup(r => r.ActionAsync(runId, It.IsAny<RunActionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, RunActionRequest, CancellationToken>((_, request, _) => captured = request)
            .ReturnsAsync(Result.Success(new RunResponse(runId, workflowId, Guid.NewGuid(), "running", 5, 2, Now, null, [])));
        var arguments = JsonDocument.Parse($$"""{"runId":"{{runId}}","workflowId":"{{workflowId}}","action":"approve_video","expectedRevision":4,"videoId":"{{videoId}}","reviewRevision":9}""").RootElement;

        var result = await Tool().ExecuteConfirmedAsync(Guid.NewGuid(), arguments, default);

        result.IsSuccess.Should().BeTrue();
        captured.Should().Match<RunActionRequest>(r => r.ExpectedRevision == 4 && r.Action == "approve_video" && r.VideoId == videoId && r.ReviewRevision == 9);
    }

    [TestMethod]
    public async Task ExecuteConfirmedAsync_WhenRevisionIsStale_PropagatesConflict()
    {
        runs.Setup(r => r.ActionAsync(It.IsAny<Guid>(), It.IsAny<RunActionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RunResponse>(Error.Conflict("StaleRevision", "Run changed.")));
        var arguments = JsonDocument.Parse($$"""{"runId":"{{runId}}","workflowId":"{{workflowId}}","action":"cancel","expectedRevision":1}""").RootElement;

        var result = await Tool().ExecuteConfirmedAsync(Guid.NewGuid(), arguments, default);

        result.IsFailure.Should().BeTrue();
    }
}
