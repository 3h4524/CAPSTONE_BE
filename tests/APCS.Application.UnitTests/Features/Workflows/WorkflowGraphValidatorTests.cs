using System.Text.Json;
using APCS.Application.Features.Workflows;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowGraphValidatorTests
{
    private readonly WorkflowGraphValidator _validator = new(new(), new());

    [TestMethod]
    public void Validate_StandardPipeline_PreservesBothApprovalGates()
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        _validator.Validate(definition).Should().BeNull();
        var shuffled = definition with { Nodes = definition.Nodes.Reverse().ToArray(), Edges = definition.Edges.Reverse().ToArray() };
        _validator.Validate(shuffled).Should().BeNull();
    }

    [TestMethod]
    [DataRow("ai_background")]
    [DataRow("ai_shot")]
    public void Validate_DisabledAiMode_ReturnsUnsupportedVideoMode(string mode)
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct(), new(Mode: mode));
        _validator.Validate(definition)!.Code.Should().Be("UnsupportedVideoMode");
    }

    [TestMethod]
    [DataRow("design-image")]
    [DataRow("generate-listing")]
    [DataRow("publish-etsy")]
    [DataRow("publish-printify")]
    public void Validate_NodeWithoutExecutor_RejectsUnsupportedNode(string type)
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        definition = definition with { Nodes = definition.Nodes.Select(x => x.Type == "apply-mockup" ? x with { Type = type } : x).ToArray() };
        _validator.Validate(definition)!.Code.Should().Be("UnsupportedNode");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public void Validate_BypassedApprovalGate_RejectsGraph(int edgeIndex)
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        definition = definition with { Edges = definition.Edges.Select((x, i) => i == edgeIndex ? x with { Target = "node-5" } : x).ToArray() };
        _validator.Validate(definition)!.Message.Should().Contain("approval gates");
    }

    [TestMethod]
    public void Validate_DuplicateNodeIdentity_RejectsGraph()
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        definition = definition with { Nodes = definition.Nodes.Select((x, i) => i == 1 ? x with { Id = "node-0" } : x).ToArray() };
        _validator.Validate(definition)!.Message.Should().Contain("unique");
    }

    [TestMethod]
    public void Validate_MalformedConfig_ReturnsValidationInsteadOfThrowing()
    {
        var definition = WorkflowTestData.Definition(new WorkflowTestData().AddProduct());
        definition = definition with { Nodes = definition.Nodes.Select(x => x.Type == "generate-video" ? x with { Config = JsonSerializer.SerializeToElement("invalid") } : x).ToArray() };
        _validator.Validate(definition)!.Message.Should().Be("Invalid node configuration.");
    }

    [TestMethod]
    public void Capabilities_StandardMvp_DeclaresFutureModesDisabled()
    {
        var capabilities = new WorkflowCapabilityRegistry().Get();
        capabilities.DefinitionVersion.Should().Be(2);
        capabilities.VideoModes.Single(x => x.Mode == "standard").Enabled.Should().BeTrue();
        capabilities.VideoModes.Where(x => x.Mode != "standard").Should().HaveCount(2)
            .And.OnlyContain(x => !x.Enabled && x.Availability == "coming_soon" && x.SupportsFallback);
        capabilities.Nodes.Where(x => x.Enabled).Select(x => x.Type).Should().Equal(WorkflowCapabilityRegistry.Pipeline);
    }
}
