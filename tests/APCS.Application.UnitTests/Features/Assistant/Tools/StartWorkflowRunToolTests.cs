using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.Confirmations;
using APCS.Application.Features.Workflows;
using APCS.Application.Features.Workflows.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class StartWorkflowRunToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IWorkflowService> workflows = new();
    private readonly Mock<IWorkflowRunService> runs = new();
    private readonly Mock<IAssistantPendingActionRepository> pendingActions = new();
    private readonly List<AssistantPendingAction> added = [];

    public StartWorkflowRunToolTests() =>
        pendingActions.Setup(r => r.AddAsync(It.IsAny<AssistantPendingAction>(), true, It.IsAny<CancellationToken>()))
            .Callback<AssistantPendingAction, bool, CancellationToken>((action, _, _) => added.Add(action)).Returns(Task.CompletedTask);

    private StartWorkflowRunTool Tool()
    {
        var registry = new WorkflowCapabilityRegistry();
        return new StartWorkflowRunTool(workflows.Object, runs.Object, new WorkflowGraphValidator(registry, new GenerateVideoConfigValidator()), pendingActions.Object, new FakeTimeProvider(Now));
    }

    private static WorkflowResponse Workflow(Guid id, bool runnable = true)
    {
        var input = new WorkflowNode("a", "product-input", "Input", new Point(), AssistantTestSupport.Json($$"""{"batchId":"{{Guid.NewGuid()}}","productId":"{{Guid.NewGuid()}}"}"""));
        var listing = new WorkflowNode("b", "generate-listing", "Listing", new Point(), AssistantTestSupport.Json("{}"));
        var definition = runnable
            ? new WorkflowDefinition(2, [input, listing], [new WorkflowEdge("e", "a", "b")])
            : new WorkflowDefinition(2, [input], []);
        return new WorkflowResponse(id, "Flow", "", definition.Nodes.Count, 7, Now, Now, definition);
    }

    private static AssistantToolContext Context(AssistantCanvasDto? canvas = null, Guid? workflowId = null) =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Seller,
            new AssistantClientContextDto("/workflows", workflowId, null, null, canvas));

    [TestMethod]
    public async Task ExecuteAsync_WhenWorkflowIsNotOwned_ReturnsServiceErrorWithoutPendingAction()
    {
        var id = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure<WorkflowResponse>(Error.NotFound("Missing", "Workflow not found.")));

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"workflowId":"{{id}}"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ExecuteAsync_WithDirtyCanvasOfSameWorkflow_ReturnsUnsavedChanges()
    {
        var id = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Workflow(id)));
        var canvas = new AssistantCanvasDto(id, 7, true, [], []);

        var outcome = await Tool().ExecuteAsync(Context(canvas), AssistantTestSupport.Json($$"""{"workflowId":"{{id}}"}"""), default);

        outcome.ModelResultJson.Should().Contain("unsaved_changes");
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ExecuteAsync_WithDirtyCanvasOfAnotherWorkflow_StillPreparesAction()
    {
        var id = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Workflow(id)));
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, true, [], []);

        var outcome = await Tool().ExecuteAsync(Context(canvas), AssistantTestSupport.Json($$"""{"workflowId":"{{id}}"}"""), default);

        outcome.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenGraphInvalid_ReturnsValidationError()
    {
        var id = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Workflow(id, runnable: false)));

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"workflowId":"{{id}}"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenValid_StoresPendingActionWithTtlAndEndsTurn()
    {
        var id = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Workflow(id)));
        var context = Context() with { ConversationId = conversationId };

        var outcome = await Tool().ExecuteAsync(context, AssistantTestSupport.Json($$"""{"workflowId":"{{id}}"}"""), default);

        outcome.EndsTurn.Should().BeTrue();
        var action = added.Should().ContainSingle().Subject;
        action.Should().Match<AssistantPendingAction>(a => a.Status == "pending" && a.UserId == AssistantTestSupport.UserId
            && a.ConversationId == conversationId && a.ToolName == "start_workflow_run" && a.ExpiresAt == Now.UtcDateTime.AddMinutes(15));
        action.Arguments.Should().Contain("\"expectedWorkflowRevision\":7");
        outcome.Events.Single().Data.Should().BeOfType<AssistantPendingActionEventData>().Which.Id.Should().Be(action.Id);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutWorkflowId_UsesWorkflowFromContext()
    {
        var id = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Workflow(id)));

        var outcome = await Tool().ExecuteAsync(Context(workflowId: id), default, default);

        outcome.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task ExecuteConfirmedAsync_StartsRunWithStoredRevisionAndIdempotencyKey()
    {
        var workflowId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        runs.Setup(r => r.StartAsync(workflowId, It.Is<StartRunRequest>(x => x.ExpectedWorkflowRevision == 7 && x.IdempotencyKey == $"assistant:{pendingId}"), cancellation.Token))
            .ReturnsAsync(Result.Success(new RunResponse(runId, workflowId, Guid.NewGuid(), "running", 1, 7, Now, null, [])));
        var arguments = JsonDocument.Parse($$"""{"workflowId":"{{workflowId}}","expectedWorkflowRevision":7}""").RootElement;

        var result = await Tool().ExecuteConfirmedAsync(pendingId, arguments, cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.Should().Contain("running");
        result.Value.Payload.GetProperty("runId").GetGuid().Should().Be(runId);
    }

    [TestMethod]
    public async Task ExecuteConfirmedAsync_WhenRunFails_PropagatesError()
    {
        runs.Setup(r => r.StartAsync(It.IsAny<Guid>(), It.IsAny<StartRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RunResponse>(Error.Conflict("StaleWorkflow", "Workflow changed.")));
        var arguments = JsonDocument.Parse($$"""{"workflowId":"{{Guid.NewGuid()}}","expectedWorkflowRevision":7}""").RootElement;

        var result = await Tool().ExecuteConfirmedAsync(Guid.NewGuid(), arguments, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("StaleWorkflow");
    }
}
