using System.Text.Json;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Dtos.Response;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.ClientActions;
using APCS.Application.Features.Assistant.Tools.Query;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Batches.Dtos;
using APCS.Application.Features.Workflows;
using APCS.Application.Features.Workflows.Validators;
using APCS.Application.UnitTests.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class WorkflowCommandToolsTests
{
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private readonly Mock<IBatchService> batches = new();

    private RunWorkflowTool RunTool()
    {
        var registry = new WorkflowCapabilityRegistry();
        return new RunWorkflowTool(new WorkflowCanvasPatchSimulator(), new WorkflowGraphValidator(registry, new GenerateVideoConfigValidator()), batches.Object);
    }

    private static AssistantCanvasDto DesignCanvas(out Guid batchId)
    {
        var product = new WorkflowTestData().AddProduct();
        batchId = product.BatchId;
        var definition = WorkflowTestData.Definition(product);
        var design = new WorkflowNode("design", "design-image", "Design image", new Point(), AssistantTestSupport.Json("{}"));
        var nodes = definition.Nodes.Append(design).ToArray();
        var edges = definition.Edges.Where(edge => edge.Source != "node-0")
            .Append(new WorkflowEdge("to-design", "node-0", "design"))
            .Append(new WorkflowEdge("from-design", "design", "node-1"))
            .ToArray();
        return new AssistantCanvasDto(WorkflowId, 4, false,
            nodes.Select(node => new AssistantCanvasNodeDto(node.Id, node.Type, node.Label, node.Config)).ToArray(),
            edges.Select(edge => new AssistantCanvasEdgeDto(edge.Id, edge.Source, edge.Target)).ToArray(),
            "Design flow");
    }

    private void WithProducts(Guid batchId, params string[] statuses) =>
        batches.Setup(service => service.ListProductsAsync(batchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<ProductDto>>(statuses
                .Select(status => new ProductDto(Guid.NewGuid(), batchId, "Tee", "tshirt", "cats", ["cat"], null, null, status, null))
                .ToArray()));

    [TestMethod]
    public async Task RunWorkflow_WhenTheBatchHasNoPendingProducts_WarnsThatThereIsNothingToDesign()
    {
        var canvas = DesignCanvas(out var batchId);
        WithProducts(batchId, "completed", "completed");

        var outcome = await RunTool().ExecuteAsync(Context(canvas), AssistantTestSupport.Json("""{"reply":"Ready.","blockedReply":"Fix it first."}"""), default);

        var payload = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject.Payload;
        payload.GetProperty("warnings")[0].GetString().Should().Contain("no pending products");
        outcome.FinalText.Should().Be("Fix it first.");
    }

    [TestMethod]
    public async Task RunWorkflow_WithAPendingProduct_HasNoWarning()
    {
        var canvas = DesignCanvas(out var batchId);
        WithProducts(batchId, "completed", "pending");

        var outcome = await RunTool().ExecuteAsync(Context(canvas), AssistantTestSupport.Json("""{"reply":"Ready.","blockedReply":"Fix it first."}"""), default);

        outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject.Payload.GetProperty("warnings").GetArrayLength().Should().Be(0);
        outcome.FinalText.Should().Be("Ready.");
    }

    private static AssistantCanvasDto Canvas(bool isDirty = false, bool connected = true) => new(
        WorkflowId, 4, isDirty,
        [
            new("input", "product-input", "Product input", AssistantTestSupport.Json($$"""{"batchId":"{{Guid.NewGuid()}}"}""")),
            new("mockup", "apply-mockup", "Apply mock-up", AssistantTestSupport.Json("{}")),
            new("gate", "approval-gate", "Mockup approval", AssistantTestSupport.Json("{}")),
            new("media", "publish-social", "Push to media", AssistantTestSupport.Json("{}"))
        ],
        connected ? [new AssistantCanvasEdgeDto("e1", "input", "mockup"), new AssistantCanvasEdgeDto("e2", "mockup", "gate"), new AssistantCanvasEdgeDto("e3", "gate", "media")] : [],
        "Etsy flow");

    private static AssistantToolContext Context(AssistantCanvasDto? canvas, AssistantCaller? caller = null) =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, caller ?? AssistantTestSupport.Seller,
            new AssistantClientContextDto("/workflows", canvas?.WorkflowId, null, null, canvas));

    [TestMethod]
    public void RunAndSave_RequireSellerAndAnOpenCanvas()
    {
        RunTool().IsAvailable(Context(Canvas())).Should().BeTrue();
        RunTool().IsAvailable(Context(null)).Should().BeFalse();
        RunTool().IsAvailable(Context(Canvas(), AssistantTestSupport.Admin)).Should().BeFalse();
        new SaveWorkflowTool().IsAvailable(Context(Canvas())).Should().BeTrue();
        new SaveWorkflowTool().IsAvailable(Context(null)).Should().BeFalse();
    }

    [TestMethod]
    public async Task RunWorkflow_EmitsARunActionWithTheReplyAndEndsTheTurn()
    {
        var outcome = await RunTool().ExecuteAsync(Context(Canvas(isDirty: true)), AssistantTestSupport.Json("""{"reply":"Press Run to start."}"""), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.EndsTurn.Should().BeTrue();
        outcome.FinalText.Should().Be("Press Run to start.");
        var action = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject;
        action.Kind.Should().Be(RunWorkflowTool.ActionKind);
        action.Payload.GetProperty("workflowId").GetGuid().Should().Be(WorkflowId);
        action.Payload.GetProperty("workflowName").GetString().Should().Be("Etsy flow");
        action.Payload.GetProperty("saveFirst").GetBoolean().Should().BeTrue();
        action.Payload.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task RunWorkflow_OnABrokenCanvas_StillOffersTheButtonButWarns()
    {
        var outcome = await RunTool().ExecuteAsync(Context(Canvas(connected: false)), AssistantTestSupport.Json("""{"reply":"Ready to run.","blockedReply":"Fix what the card lists first."}"""), default);

        outcome.Succeeded.Should().BeTrue();
        var payload = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject.Payload;
        payload.GetProperty("warnings").GetArrayLength().Should().Be(1);
        outcome.ModelResultJson.Should().Contain("cannot run as it is");
        outcome.FinalText.Should().Be("Fix what the card lists first.");
    }

    [TestMethod]
    public async Task SaveWorkflow_WithoutUnsavedChanges_ReturnsAnError()
    {
        var outcome = await new SaveWorkflowTool().ExecuteAsync(Context(Canvas()), AssistantTestSupport.Json("""{"reply":"Save it."}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.EndsTurn.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("nothing_to_save");
    }

    [TestMethod]
    public async Task SaveWorkflow_WithUnsavedChanges_EmitsASaveAction()
    {
        var outcome = await new SaveWorkflowTool().ExecuteAsync(Context(Canvas(isDirty: true)), AssistantTestSupport.Json("""{"reply":"Save it."}"""), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.EndsTurn.Should().BeTrue();
        var action = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject;
        action.Kind.Should().Be(SaveWorkflowTool.ActionKind);
        action.Payload.GetProperty("workflowName").GetString().Should().Be("Etsy flow");
    }

    [TestMethod]
    public async Task EstimateRunCost_UsesTheOpenWorkflowAndSaysTheCanvasIsUnsaved()
    {
        var estimates = new Mock<IRunCostEstimateService>();
        estimates.Setup(service => service.EstimateAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RunCostEstimateResponse(
                [new RunCostItem("design", "Design images", "8 images", 8, 0.32m)], 0.32m, 0m, "Estimate only.")));

        var outcome = await new EstimateRunCostTool(estimates.Object).ExecuteAsync(Context(Canvas(isDirty: true)), AssistantTestSupport.Json("{}"), default);

        outcome.Succeeded.Should().BeTrue();
        var result = JsonDocument.Parse(outcome.ModelResultJson).RootElement;
        result.GetProperty("totalUsd").GetDecimal().Should().Be(0.32m);
        result.GetProperty("items")[0].GetProperty("label").GetString().Should().Be("Design images");
        result.GetProperty("basedOn").GetString().Should().Contain("unsaved");
    }

    [TestMethod]
    public async Task EstimateRunCost_WithoutAWorkflow_AsksWhichOne()
    {
        var outcome = await new EstimateRunCostTool(Mock.Of<IRunCostEstimateService>()).ExecuteAsync(Context(null), AssistantTestSupport.Json("{}"), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("missing_workflow");
    }

    [TestMethod]
    public async Task EstimateRunCost_WhenTheServiceFails_ReturnsItsError()
    {
        var estimates = new Mock<IRunCostEstimateService>();
        estimates.Setup(service => service.EstimateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RunCostEstimateResponse>(Error.NotFound("WorkflowNotFound", "Workflow not found.")));

        var outcome = await new EstimateRunCostTool(estimates.Object).ExecuteAsync(Context(null), AssistantTestSupport.Json($$"""{"workflowId":"{{WorkflowId}}"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("Workflow not found");
    }

    [TestMethod]
    public async Task GetRunProgress_UsesTheOpenWorkflowAndReturnsTheNextStep()
    {
        var reader = new Mock<IAssistantRunProgressReader>();
        reader.Setup(service => service.ReadAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AssistantRunProgressResponseDto(
                WorkflowId, "Etsy flow", AssistantRunStages.DesignApproval, AssistantRunStates.WaitingForUser, "Approve the designs.", WaitingCount: 3)));

        var outcome = await new GetRunProgressTool(reader.Object).ExecuteAsync(Context(Canvas()), AssistantTestSupport.Json("{}"), default);

        outcome.Succeeded.Should().BeTrue();
        var result = JsonDocument.Parse(outcome.ModelResultJson).RootElement;
        result.GetProperty("stage").GetString().Should().Be(AssistantRunStages.DesignApproval);
        result.GetProperty("nextStep").GetString().Should().Be("Approve the designs.");
        result.GetProperty("waitingCount").GetInt32().Should().Be(3);
    }

    [TestMethod]
    public async Task GetRunProgress_WithoutAWorkflow_AsksWhichOne()
    {
        var outcome = await new GetRunProgressTool(Mock.Of<IAssistantRunProgressReader>()).ExecuteAsync(Context(null), AssistantTestSupport.Json("{}"), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("missing_workflow");
    }
}
