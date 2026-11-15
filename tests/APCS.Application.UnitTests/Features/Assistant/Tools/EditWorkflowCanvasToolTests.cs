using APCS.Application.Features.Assistant.Catalog;
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
        var args = """{"operations":[{"op":"add_node","ref":"n1","type":"generate-listing","label":"Listing","after":"a"},{"op":"connect","source":"a","target":"n1"},{"op":"update_config","nodeId":"n1","configJson":"{\"tone\":\"playful\"}"}]}""";

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json(args), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.EndsTurn.Should().BeTrue();
        var action = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject;
        action.Kind.Should().Be("edit_workflow_canvas");
        var raw = action.Payload.GetRawText();
        raw.Should().Contain("\"config\"").And.Contain("playful").And.NotContain("configJson");
        action.Payload.GetProperty("operations").GetArrayLength().Should().Be(3);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenGraphIsNotRunnable_ReturnsWarningsButStillProposes()
    {
        var args = """{"operations":[{"op":"add_node","ref":"n1","type":"generate-listing"}]}""";

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json(args), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>()
            .Which.Payload.GetProperty("warnings").GetArrayLength().Should().BeGreaterThan(0);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithReply_CarriesItAsTheFinalText()
    {
        var args = """{"operations":[{"op":"add_node","ref":"n1","type":"generate-listing"}],"reply":"Review and apply the change."}""";

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json(args), default);

        outcome.FinalText.Should().Be("Review and apply the change.");
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutReply_LeavesTheFinalAnswerToTheModel()
    {
        var args = """{"operations":[{"op":"add_node","ref":"n1","type":"generate-listing"}]}""";

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json(args), default);

        outcome.FinalText.Should().BeNull();
    }

    [TestMethod]
    public void Declaration_OffersEveryCatalogStepTypeIncludingThePublishSteps()
    {
        var types = Tool().Declaration.ParametersSchema
            .GetProperty("properties").GetProperty("operations").GetProperty("items")
            .GetProperty("properties").GetProperty("type").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()).ToArray();

        types.Should().BeEquivalentTo(WorkflowNodeCatalog.Nodes.Select(node => node.Type));
        types.Should().Contain(["publish-printify", "publish-etsy"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_AddPushToPrintify_IsProposed()
    {
        var context = CanvasContext(
            [new("g", "approval-gate", "Mockup Approval", AssistantTestSupport.Json("{}"))],
            []);
        var args = """{"operations":[{"op":"add_node","ref":"p","type":"publish-printify","after":"g"},{"op":"connect","source":"g","target":"p"}]}""";

        var outcome = await Tool().ExecuteAsync(context, AssistantTestSupport.Json(args), default);

        outcome.Succeeded.Should().BeTrue();
    }

    [TestMethod]
    public async Task ExecuteAsync_AddNodeWithConfig_SendsTheConfigOnTheAddOperation()
    {
        var args = """{"operations":[{"op":"add_node","ref":"n1","type":"generate-listing","configJson":"{\"tone\":\"playful\"}"}]}""";

        var outcome = await Tool().ExecuteAsync(Context(), AssistantTestSupport.Json(args), default);

        var operation = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject.Payload.GetProperty("operations")[0];
        operation.GetProperty("op").GetString().Should().Be("add_node");
        operation.GetProperty("config").GetProperty("tone").GetString().Should().Be("playful");
    }

    [TestMethod]
    public async Task ExecuteAsync_WarningTheCanvasAlreadyHad_IsNotRepeated()
    {
        var context = CanvasContext(
            [
                new("a", "product-input", "Input", AssistantTestSupport.Json($$"""{"batchId":"{{Guid.NewGuid()}}","productId":"{{Guid.NewGuid()}}"}""")),
                new("d", "design-image", "Design image", AssistantTestSupport.Json("{}"))
            ],
            []);
        var args = """{"operations":[{"op":"add_node","ref":"m","type":"apply-mockup"}]}""";

        var outcome = await Tool().ExecuteAsync(context, AssistantTestSupport.Json(args), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>()
            .Which.Payload.GetProperty("warnings").GetArrayLength().Should().Be(0);
        outcome.ModelResultJson.Should().Contain("\"warnings\":[]");
    }

    [TestMethod]
    public async Task ExecuteAsync_WarningThePatchIntroduces_IsReturned()
    {
        var context = CanvasContext(
            [
                new("a", "product-input", "Input", AssistantTestSupport.Json($$"""{"batchId":"{{Guid.NewGuid()}}","productId":"{{Guid.NewGuid()}}"}""")),
                new("l", "generate-listing", "Listing content", AssistantTestSupport.Json("{}"))
            ],
            [new("e", "a", "l")]);
        var args = """{"operations":[{"op":"disconnect","source":"a","target":"l"}]}""";

        var outcome = await Tool().ExecuteAsync(context, AssistantTestSupport.Json(args), default);

        outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>()
            .Which.Payload.GetProperty("warnings").EnumerateArray().Should().ContainSingle()
            .Which.GetString().Should().Contain("in one line");
    }

    private static AssistantToolContext CanvasContext(IReadOnlyList<AssistantCanvasNodeDto> nodes, IReadOnlyList<AssistantCanvasEdgeDto> edges) =>
        new(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Seller,
            new AssistantClientContextDto("/workflows", null, null, null, new AssistantCanvasDto(Guid.NewGuid(), 1, false, nodes, edges)));
}
