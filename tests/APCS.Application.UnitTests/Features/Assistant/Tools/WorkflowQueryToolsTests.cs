using System.Text.Json;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.Query;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class WorkflowQueryToolsTests
{
    private readonly Mock<IWorkflowService> workflows = new();
    private readonly Mock<IWorkflowRunService> runs = new();

    private static AssistantToolContext Context(AssistantClientContextDto? client = null, AssistantCaller? caller = null) =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, caller ?? AssistantTestSupport.Seller, client);

    private static WorkflowResponse Workflow(Guid? id = null, string? config = null) => new(
        id ?? Guid.NewGuid(), "Flow", "desc", 1, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        new WorkflowDefinition(2, [new WorkflowNode("n1", "product-input", "Input", new Point(), AssistantTestSupport.Json(config ?? """{"a":1}"""))], [new WorkflowEdge("e1", "n1", "n2")]));

    private static RunResponse Run(Guid? id = null) => new(
        id ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "failed", 2, 3, DateTimeOffset.UnixEpoch, "boom",
        [new NodeRunResponse(Guid.NewGuid(), "n1", "generate-video", "failed", 1, "render", 40, "No approved mockups", AssistantTestSupport.Json("""{"secret":"payload"}"""))]);

    [TestMethod]
    public async Task ListWorkflows_ReturnsAtMostTwentyMappedItems()
    {
        workflows.Setup(w => w.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<WorkflowResponse>>(Enumerable.Range(0, 30).Select(_ => Workflow()).ToArray()));

        var outcome = await new ListWorkflowsTool(workflows.Object).ExecuteAsync(Context(), default, default);

        JsonDocument.Parse(outcome.ModelResultJson).RootElement.GetProperty("workflows").GetArrayLength().Should().Be(20);
    }

    [TestMethod]
    public void WorkflowTools_AreSellerOnly()
    {
        new ListWorkflowsTool(workflows.Object).IsAvailable(Context(caller: AssistantTestSupport.Admin)).Should().BeFalse();
        new GetWorkflowRunTool(runs.Object).IsAvailable(Context(caller: AssistantTestSupport.Admin)).Should().BeFalse();
    }

    [TestMethod]
    public async Task GetWorkflow_WhenServiceFails_ReturnsServiceErrorCode()
    {
        workflows.Setup(w => w.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<WorkflowResponse>(Error.NotFound("Missing", "Workflow not found.")));

        var outcome = await new GetWorkflowTool(workflows.Object).ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"workflowId":"{{Guid.NewGuid()}}"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("missing");
    }

    [TestMethod]
    public async Task GetWorkflow_ClipsLongNodeConfig()
    {
        var id = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Workflow(id, "{\"v\":\"" + new string('z', 3000) + "\"}")));

        var outcome = await new GetWorkflowTool(workflows.Object).ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"workflowId":"{{id}}"}"""), default);

        outcome.ModelResultJson.Should().NotContain(new string('z', 1600));
    }

    [TestMethod]
    public async Task GetWorkflow_WithoutId_UsesWorkflowFromContext()
    {
        var id = Guid.NewGuid();
        workflows.Setup(w => w.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Workflow(id)));
        var client = new AssistantClientContextDto("/workflows", id, null, null, null);

        var outcome = await new GetWorkflowTool(workflows.Object).ExecuteAsync(Context(client), default, default);

        outcome.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetWorkflowRun_PrefersRunIdThenWorkflowIdThenContext()
    {
        var runId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        runs.Setup(r => r.GetAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Run(runId)));
        runs.Setup(r => r.GetLatestAsync(workflowId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Run()));
        var tool = new GetWorkflowRunTool(runs.Object);

        await tool.ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"runId":"{{runId}}","workflowId":"{{workflowId}}"}"""), default);
        runs.Verify(r => r.GetAsync(runId, It.IsAny<CancellationToken>()), Times.Once);
        runs.Verify(r => r.GetLatestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        await tool.ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"workflowId":"{{workflowId}}"}"""), default);
        runs.Verify(r => r.GetLatestAsync(workflowId, It.IsAny<CancellationToken>()), Times.Once);

        var client = new AssistantClientContextDto("/workflows", workflowId, runId, null, null);
        await tool.ExecuteAsync(Context(client), default, default);
        runs.Verify(r => r.GetAsync(runId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task GetWorkflowRun_WithoutAnyId_ReturnsMissingRun()
    {
        var outcome = await new GetWorkflowRunTool(runs.Object).ExecuteAsync(Context(), default, default);

        outcome.ModelResultJson.Should().Contain("missing_run");
    }

    [TestMethod]
    public async Task GetWorkflowRun_ReturnsNodeStatusButNotNodeOutput()
    {
        var id = Guid.NewGuid();
        runs.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(Run(id)));

        var outcome = await new GetWorkflowRunTool(runs.Object).ExecuteAsync(Context(), AssistantTestSupport.Json($$"""{"runId":"{{id}}"}"""), default);

        outcome.ModelResultJson.Should().Contain("No approved mockups").And.Contain("generate-video").And.NotContain("payload");
    }

    [TestMethod]
    public async Task GetWorkflowCapabilities_ReturnsRegistryContent()
    {
        var outcome = await new GetWorkflowCapabilitiesTool(new WorkflowCapabilityRegistry()).ExecuteAsync(Context(), default, default);

        outcome.ModelResultJson.Should().Contain("product-input").And.Contain("ai_shot");
    }
}
