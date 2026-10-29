using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.ClientActions;
using APCS.Application.Features.Workflows;
using APCS.Application.Features.Workflows.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class EditWorkflowCanvasToolTests
{
    private static EditWorkflowCanvasTool Tool()
    {
        var registry = new WorkflowCapabilityRegistry();
        return new EditWorkflowCanvasTool(new WorkflowCanvasPatchSimulator(), registry, new WorkflowGraphValidator(registry, new GenerateVideoConfigValidator()));
    }

    private static AssistantToolContext Context(bool withCanvas = true, AssistantCaller? caller = null)
    {
        var canvas = withCanvas
            ? new AssistantCanvasDto(
                Guid.NewGuid(), 1, false,
                [new("a", "product-input", "Input", AssistantTestSupport.Json($$"""{"batchId":"{{Guid.NewGuid()}}","productId":"{{Guid.NewGuid()}}"}"""))],
                [])
            : null;
        return new(Guid.NewGuid(), AssistantTestSupport.UserId, caller ?? AssistantTestSupport.Seller,
            new AssistantClientContextDto("/workflows", null, null, null, canvas));
    }

    [TestMethod]
    public void IsAvailable_RequiresSellerAndCanvas()
    {
        Tool().IsAvailable(Context()).Should().BeTrue();
        Tool().IsAvailable(Context(withCanvas: false)).Should().BeFalse();
        Tool().IsAvailable(Context(caller: AssistantTestSupport.Admin)).Should().BeFalse();
    }

    [TestMethod]
    public async Task ExecuteAsync_WithInvalidOperations_ReturnsErrorAndDoesNotEndTurn()
    {
        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"operations":[{"op":"connect","source":"a","target":"zzz"}]}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.EndsTurn.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("invalid_operations");
    }

    [TestMethod]
    public async Task ExecuteAsync_WithValidOperations_EmitsClientActionWithParsedConfigAndEndsTurn()
    {
        var args = """{"operations":[{"op":"add_node","ref":"n1","type":"generate-listing","label":"Listing","after":"a"},{"op":"connect","source":"a","target":"n1"},{"op":"update_config","nodeId":"n1","configJson":"{\"tone\":\"warm\"}"}]}""";

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json(args), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.EndsTurn.Should().BeTrue();
        var action = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject;
        action.Kind.Should().Be("edit_workflow_canvas");
        var raw = action.Payload.GetRawText();
        raw.Should().Contain("\"config\"").And.Contain("warm").And.NotContain("configJson");
        action.Payload.GetProperty("operations").GetArrayLength().Should().Be(3);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenGraphIsNotRunnable_ReturnsWarningsButStillProposes()
    {
        var args = """{"operations":[{"op":"rename_node","nodeId":"a","label":"Renamed"}]}""";

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json(args), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>()
            .Which.Payload.GetProperty("warnings").GetArrayLength().Should().BeGreaterThan(0);
    }
}
