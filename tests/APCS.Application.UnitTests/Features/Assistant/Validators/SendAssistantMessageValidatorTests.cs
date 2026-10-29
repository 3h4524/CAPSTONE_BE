using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Validators;

[TestClass]
public sealed class SendAssistantMessageValidatorTests
{
    private readonly SendAssistantMessageValidator validator = new();

    private static AssistantClientContextDto Context(
        string route = "/workflows",
        IReadOnlyList<string>? selected = null,
        AssistantCanvasDto? canvas = null) => new(route, null, null, selected, canvas);

    private static AssistantCanvasNodeDto Node(int index, string? config = null) =>
        new($"n{index}", "product-input", "Label", AssistantTestSupport.Json(config ?? "{}"));

    [TestMethod]
    [DataRow("", false)]
    [DataRow("   ", false)]
    [DataRow("a", true)]
    public void Validate_Message_RejectsBlank(string message, bool valid) =>
        validator.Validate(new SendAssistantMessageRequestDto(null, message, null)).IsValid.Should().Be(valid);

    [TestMethod]
    [DataRow(4000, true)]
    [DataRow(4001, false)]
    public void Validate_MessageLength_Boundary(int length, bool valid) =>
        validator.Validate(new SendAssistantMessageRequestDto(null, new string('a', length), null)).IsValid.Should().Be(valid);

    [TestMethod]
    [DataRow("/workflows", true)]
    [DataRow("workflows", false)]
    [DataRow("", false)]
    public void Validate_Route_MustStartWithSlash(string route, bool valid) =>
        validator.Validate(new SendAssistantMessageRequestDto(null, "hi", Context(route))).IsValid.Should().Be(valid);

    [TestMethod]
    public void Validate_RouteLongerThanLimit_IsRejected() =>
        validator.Validate(new SendAssistantMessageRequestDto(null, "hi", Context("/" + new string('a', 300)))).IsValid.Should().BeFalse();

    [TestMethod]
    [DataRow(50, true)]
    [DataRow(51, false)]
    public void Validate_SelectedNodeIds_Boundary(int count, bool valid) =>
        validator.Validate(new SendAssistantMessageRequestDto(null, "hi",
            Context(selected: Enumerable.Range(0, count).Select(i => $"n{i}").ToArray()))).IsValid.Should().Be(valid);

    [TestMethod]
    [DataRow(40, true)]
    [DataRow(41, false)]
    public void Validate_CanvasNodeCount_Boundary(int count, bool valid)
    {
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, Enumerable.Range(0, count).Select(i => Node(i)).ToArray(), []);

        validator.Validate(new SendAssistantMessageRequestDto(null, "hi", Context(canvas: canvas))).IsValid.Should().Be(valid);
    }

    [TestMethod]
    [DataRow(80, true)]
    [DataRow(81, false)]
    public void Validate_CanvasEdgeCount_Boundary(int count, bool valid)
    {
        var edges = Enumerable.Range(0, count).Select(i => new AssistantCanvasEdgeDto($"e{i}", "a", "b")).ToArray();
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, [], edges);

        validator.Validate(new SendAssistantMessageRequestDto(null, "hi", Context(canvas: canvas))).IsValid.Should().Be(valid);
    }

    [TestMethod]
    public void Validate_OversizedNodeConfig_IsRejected()
    {
        var config = "{\"v\":\"" + new string('x', 8200) + "\"}";
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, [Node(1, config)], []);

        validator.Validate(new SendAssistantMessageRequestDto(null, "hi", Context(canvas: canvas))).IsValid.Should().BeFalse();
    }
}
