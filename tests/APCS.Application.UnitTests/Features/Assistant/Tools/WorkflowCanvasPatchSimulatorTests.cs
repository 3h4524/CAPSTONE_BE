using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools.ClientActions;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class WorkflowCanvasPatchSimulatorTests
{
    private static readonly IReadOnlySet<string> Allowed = new HashSet<string> { "product-input", "apply-mockup", "generate-listing" };

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
    public void Simulate_UpdateConfig_MergesShallowly()
    {
        var result = Run(Op("update_config", nodeId: "a", configJson: """{"added":2}"""));

        var config = result.Result.Nodes.Single(node => node.Id == "a").Config;
        config.GetProperty("keep").GetInt32().Should().Be(1);
        config.GetProperty("added").GetInt32().Should().Be(2);
        result.Operations.Single().Config!.Value.GetProperty("added").GetInt32().Should().Be(2);
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
}
