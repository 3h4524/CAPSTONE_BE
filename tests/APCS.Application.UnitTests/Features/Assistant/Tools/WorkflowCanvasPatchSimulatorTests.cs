using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools.ClientActions;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class WorkflowCanvasPatchSimulatorTests
{
    private static readonly IReadOnlySet<string> Allowed = new HashSet<string>
    {
        "product-input", "prompt-synthesis", "design-image", "design-approval", "apply-mockup", "approval-gate", "generate-video", "generate-listing"
    };

    private readonly WorkflowCanvasPatchSimulator simulator = new();

    private static AssistantCanvasDto Canvas() => new(
        Guid.NewGuid(), 1, false,
        [
            new("a", "product-input", "Input", AssistantTestSupport.Json("""{"keep":1}""")),
            new("b", "apply-mockup", "Mockup", AssistantTestSupport.Json("{}"))
        ],
        [new("e1", "a", "b")]);

    private static CanvasOperationArgs Op(string op, string? @ref = null, string? type = null, string? label = null, string? after = null,
        string? nodeId = null, string? source = null, string? target = null, string? configJson = null) =>
        new(op, @ref, type, label, after, nodeId, source, target, configJson);

    private CanvasPatchSimulation Run(params CanvasOperationArgs[] operations) => simulator.Simulate(Canvas(), operations, Allowed);

    [TestMethod]
    public void Simulate_AddNodeThenConnectByRef_AppliesBoth()
    {
        var result = Run(Op("add_node", "n1", "generate-listing", "Listing", after: "a"), Op("connect", source: "a", target: "n1"));

        result.Errors.Should().BeEmpty();
        result.Operations.Should().HaveCount(2);
        result.Result.Nodes.Select(node => node.Id).Should().Contain("n1");
        result.Result.Edges.Should().Contain(edge => edge.Source == "a" && edge.Target == "n1");
    }

    [TestMethod]
    public void Simulate_DuplicateRef_IsRejected() =>
        Run(Op("add_node", "n1", "generate-listing"), Op("add_node", "n1", "generate-listing")).Errors.Should().ContainSingle().Which.Should().Contain("already used");

    [TestMethod]
    public void Simulate_RefThatCollidesWithExistingId_IsRejected() =>
        Run(Op("add_node", "a", "generate-listing")).Errors.Should().ContainSingle();

    [TestMethod]
    public void Simulate_TypeNotEnabled_IsRejected() =>
        Run(Op("add_node", "n1", "publish-etsy")).Errors.Should().ContainSingle().Which.Should().Contain("not available");

    [TestMethod]
    public void Simulate_ConnectToSelf_IsRejected() =>
        Run(Op("connect", source: "a", target: "a")).Errors.Should().ContainSingle();

    [TestMethod]
    public void Simulate_DuplicateConnection_IsRejected() =>
        Run(Op("connect", source: "a", target: "b")).Errors.Should().ContainSingle().Which.Should().Contain("already connected");

    [TestMethod]
    public void Simulate_ConnectUnknownNode_IsRejected() =>
        Run(Op("connect", source: "a", target: "zzz")).Errors.Should().ContainSingle();

    [TestMethod]
    public void Simulate_DisconnectMissingEdge_IsRejected() =>
        Run(Op("disconnect", source: "b", target: "a")).Errors.Should().ContainSingle();

    [TestMethod]
    public void Simulate_Disconnect_RemovesEdge() =>
        Run(Op("disconnect", source: "a", target: "b")).Result.Edges.Should().BeEmpty();

    [TestMethod]
    public void Simulate_RemoveNode_AlsoRemovesItsEdges()
    {
        var result = Run(Op("remove_node", nodeId: "b"));

        result.Result.Nodes.Should().ContainSingle();
        result.Result.Edges.Should().BeEmpty();
    }

    [TestMethod]
    public void Simulate_UpdateConfig_KeepsExistingKeysAndAddsTheNewOne()
    {
        var id = Guid.NewGuid();

        var result = Run(Op("update_config", nodeId: "a", configJson: $$"""{"batchId":"{{id}}"}"""));

        var config = result.Result.Nodes.Single(node => node.Id == "a").Config;
        config.GetProperty("keep").GetInt32().Should().Be(1);
        config.GetProperty("batchId").GetString().Should().Be(id.ToString());
        result.Operations.Single().Config!.Value.GetProperty("batchId").GetString().Should().Be(id.ToString());
    }

    [TestMethod]
    [DataRow("[1,2]")]
    [DataRow("not json")]
    [DataRow("")]
    public void Simulate_UpdateConfigThatIsNotAnObject_IsRejected(string configJson) =>
        Run(Op("update_config", nodeId: "a", configJson: configJson)).Errors.Should().ContainSingle();

    [TestMethod]
    public void Simulate_UpdateConfigLargerThanLimit_IsRejected() =>
        Run(Op("update_config", nodeId: "a", configJson: "{\"v\":\"" + new string('x', 8200) + "\"}")).Errors.Should().ContainSingle().Which.Should().Contain("too large");

    [TestMethod]
    public void Simulate_RenameNode_ChangesLabel() =>
        Run(Op("rename_node", nodeId: "a", label: "New")).Result.Nodes.Single(node => node.Id == "a").Label.Should().Be("New");

    [TestMethod]
    public void Simulate_RenameWithBlankLabel_IsRejected() =>
        Run(Op("rename_node", nodeId: "a", label: " ")).Errors.Should().ContainSingle();

    [TestMethod]
    public void Simulate_UnknownOperation_IsRejected() =>
        Run(Op("explode")).Errors.Should().ContainSingle().Which.Should().Contain("Unknown operation");

    [TestMethod]
    public void Simulate_NoOperations_IsRejected() => Run().Errors.Should().ContainSingle();

    [TestMethod]
    public void Simulate_MoreThanTwentyOperations_IsRejected() =>
        simulator.Simulate(Canvas(), Enumerable.Range(0, 21).Select(i => Op("rename_node", nodeId: "a", label: $"L{i}")).ToArray(), Allowed)
            .Errors.Should().NotBeEmpty();

    [TestMethod]
    public void Simulate_DoesNotMutateTheSourceCanvas()
    {
        var canvas = Canvas();

        simulator.Simulate(canvas, [Op("remove_node", nodeId: "a")], Allowed);

        canvas.Nodes.Should().HaveCount(2);
        canvas.Edges.Should().HaveCount(1);
    }

    private static AssistantCanvasDto PipelineCanvas(params (string Id, string Type)[] steps) => new(
        Guid.NewGuid(), 1, false,
        steps.Select(step => new AssistantCanvasNodeDto(step.Id, step.Type, step.Type, AssistantTestSupport.Json("{}"))).ToArray(),
        []);

    [TestMethod]
    public void Simulate_ConnectingToTheNextPresentStep_IsAllowed()
    {
        var canvas = PipelineCanvas(("a", "product-input"), ("b", "apply-mockup"), ("c", "generate-video"));

        var result = simulator.Simulate(canvas, [Op("connect", source: "a", target: "b"), Op("connect", source: "b", target: "c")], Allowed);

        result.Errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Simulate_SkippingAPresentStep_IsRejectedWithTheNextStepName()
    {
        var canvas = PipelineCanvas(("a", "product-input"), ("b", "apply-mockup"), ("c", "generate-video"));

        var result = simulator.Simulate(canvas, [Op("connect", source: "a", target: "c")], Allowed);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Steps run in order").And.Contain("Apply mock-up");
    }

    [TestMethod]
    public void Simulate_ConnectingBackwardsInRunOrder_IsRejected()
    {
        var canvas = PipelineCanvas(("a", "product-input"), ("b", "apply-mockup"));

        var result = simulator.Simulate(canvas, [Op("connect", source: "b", target: "a")], Allowed);

        result.Errors.Should().ContainSingle().Which.Should().Contain("last step");
    }

    [TestMethod]
    public void Simulate_ListingStepIsOutsideTheRunOrder_AndCanConnectToAnyStep()
    {
        var canvas = PipelineCanvas(("a", "product-input"), ("b", "apply-mockup"), ("l", "generate-listing"));

        var result = simulator.Simulate(canvas, [Op("connect", source: "b", target: "l"), Op("connect", source: "a", target: "b")], Allowed);

        result.Errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Simulate_LoopThroughListingStep_IsRejected()
    {
        var canvas = PipelineCanvas(("a", "product-input"), ("l", "generate-listing"));

        var result = simulator.Simulate(canvas, [Op("connect", source: "a", target: "l"), Op("connect", source: "l", target: "a")], Allowed);

        result.Errors.Should().ContainSingle().Which.Should().Contain("loop");
    }

    [TestMethod]
    public void Simulate_InsertingAStepBetweenTwoConnected_NeedsDisconnectFirst()
    {
        var canvas = new AssistantCanvasDto(
            Guid.NewGuid(), 1, false,
            [new("a", "product-input", "In", AssistantTestSupport.Json("{}")), new("c", "apply-mockup", "Mock", AssistantTestSupport.Json("{}"))],
            [new("e", "a", "c")]);

        var result = simulator.Simulate(canvas,
            [Op("add_node", "n1", "design-image"), Op("disconnect", source: "a", target: "c"), Op("connect", source: "a", target: "n1"), Op("connect", source: "n1", target: "c")], Allowed);

        result.Errors.Should().BeEmpty();
        result.Result.Edges.Select(edge => (edge.Source, edge.Target)).Should().BeEquivalentTo([("a", "n1"), ("n1", "c")]);
    }

    [TestMethod]
    public void Simulate_UpdateConfig_RejectsUnknownSettingAndListsValidOnes()
    {
        var canvas = PipelineCanvas(("d", "design-image"));

        var result = simulator.Simulate(canvas, [Op("update_config", nodeId: "d", configJson: """{"quality":"high"}""")], Allowed);

        result.Errors.Should().ContainSingle().Which.Should().Contain("'quality' is not a setting").And.Contain("imageProvider");
    }

    [TestMethod]
    [DataRow("""{"imageProvider":"midjourney"}""")]
    [DataRow("""{"variants":9}""")]
    [DataRow("""{"variants":1.5}""")]
    [DataRow("""{"aspectRatio":"2:1"}""")]
    public void Simulate_UpdateConfig_RejectsValuesOutsideTheCatalog(string configJson)
    {
        var canvas = PipelineCanvas(("d", "design-image"));

        simulator.Simulate(canvas, [Op("update_config", nodeId: "d", configJson: configJson)], Allowed).Errors.Should().ContainSingle();
    }

    [TestMethod]
    public void Simulate_UpdateConfig_AcceptsValidValues()
    {
        var canvas = PipelineCanvas(("d", "design-image"));

        var result = simulator.Simulate(canvas, [Op("update_config", nodeId: "d", configJson: """{"imageProvider":"ideogram","variants":3,"aspectRatio":"9:16"}""")], Allowed);

        result.Errors.Should().BeEmpty();
        result.Result.Nodes.Single().Config.GetProperty("imageProvider").GetString().Should().Be("ideogram");
    }

    [TestMethod]
    public void Simulate_UpdateConfig_ExpandsDottedKeysAndMergesNestedObjectsDeeply()
    {
        var canvas = new AssistantCanvasDto(
            Guid.NewGuid(), 1, false,
            [new("v", "generate-video", "Video", AssistantTestSupport.Json("""{"standardOptions":{"motionPreset":"zoom","transition":"none"}}"""))],
            []);

        var result = simulator.Simulate(canvas, [Op("update_config", nodeId: "v", configJson: """{"standardOptions.transition":"fade"}""")], Allowed);

        result.Errors.Should().BeEmpty();
        var options = result.Result.Nodes.Single().Config.GetProperty("standardOptions");
        options.GetProperty("transition").GetString().Should().Be("fade");
        options.GetProperty("motionPreset").GetString().Should().Be("zoom");
    }

    [TestMethod]
    public void Simulate_UpdateConfig_OnAStepWithoutSettings_IsRejected()
    {
        var canvas = PipelineCanvas(("e", "export-zip"));

        simulator.Simulate(canvas, [Op("update_config", nodeId: "e", configJson: """{"x":1}""")], new HashSet<string> { "export-zip" })
            .Errors.Should().ContainSingle().Which.Should().Contain("no settings");
    }

    [TestMethod]
    public void Simulate_UpdateConfig_RejectsMalformedIds()
    {
        var canvas = PipelineCanvas(("p", "prompt-synthesis"));

        var result = simulator.Simulate(canvas, [Op("update_config", nodeId: "p", configJson: """{"designTemplateId":"not-a-guid"}""")], Allowed);

        result.Errors.Should().ContainSingle().Which.Should().Contain("designTemplateId");
    }

    [TestMethod]
    public void Simulate_UpdateConfig_AllowsAnEmptyOptionalStyle()
    {
        var canvas = PipelineCanvas(("p", "prompt-synthesis"));

        simulator.Simulate(canvas, [Op("update_config", nodeId: "p", configJson: """{"stylePresetId":""}""")], Allowed).Errors.Should().BeEmpty();
    }
}
