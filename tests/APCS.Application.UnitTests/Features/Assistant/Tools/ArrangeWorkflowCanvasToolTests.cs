using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.ClientActions;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class ArrangeWorkflowCanvasToolTests
{
    private static AssistantToolContext Context(int nodes = 3, bool withCanvas = true, AssistantCaller? caller = null)
    {
        var canvas = withCanvas
            ? new AssistantCanvasDto(
                Guid.NewGuid(), 1, false,
                Enumerable.Range(0, nodes).Select(i => new AssistantCanvasNodeDto($"n{i}", "product-input", "N", AssistantTestSupport.Json("{}"))).ToArray(),
                [])
            : null;
        return new(Guid.NewGuid(), AssistantTestSupport.UserId, caller ?? AssistantTestSupport.Seller, new AssistantClientContextDto("/workflows", null, null, null, canvas));
    }

    private static Task<AssistantToolOutcome> Run(string args, AssistantToolContext? context = null) =>
        new ArrangeWorkflowCanvasTool().ExecuteAsync(context ?? Context(), AssistantTestSupport.Json(args), default);

    [TestMethod]
    public void IsAvailable_RequiresSellerAndAnOpenCanvas()
    {
        var tool = new ArrangeWorkflowCanvasTool();

        tool.IsAvailable(Context()).Should().BeTrue();
        tool.IsAvailable(Context(withCanvas: false)).Should().BeFalse();
        tool.IsAvailable(Context(caller: AssistantTestSupport.Admin)).Should().BeFalse();
    }

    [TestMethod]
    [DataRow("horizontal")]
    [DataRow("vertical")]
    [DataRow("grid")]
    public async Task ExecuteAsync_WithAValidLayout_EmitsAClientActionAndEndsTheTurn(string layout)
    {
        var outcome = await Run($$"""{"layout":"{{layout}}","reply":"Here is a tidier layout."}""");

        outcome.Succeeded.Should().BeTrue();
        outcome.EndsTurn.Should().BeTrue();
        outcome.FinalText.Should().Be("Here is a tidier layout.");
        var action = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject;
        action.Kind.Should().Be("arrange_workflow_canvas");
        action.Payload.GetProperty("layout").GetString().Should().Be(layout);
        action.Payload.GetProperty("spacing").GetString().Should().Be("comfortable");
        action.Payload.GetProperty("perRow").GetInt32().Should().Be(3);
    }

    [TestMethod]
    public async Task ExecuteAsync_NormalisesCaseAndKeepsTheRequestedSpacing()
    {
        var outcome = await Run("""{"layout":"GRID","spacing":"Wide","perRow":4,"reply":"ok"}""");

        var payload = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject.Payload;
        payload.GetProperty("layout").GetString().Should().Be("grid");
        payload.GetProperty("spacing").GetString().Should().Be("wide");
        payload.GetProperty("perRow").GetInt32().Should().Be(4);
    }

    [TestMethod]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    [DataRow(9, 6)]
    public async Task ExecuteAsync_ClampsPerRow(int requested, int expected)
    {
        var outcome = await Run($$"""{"layout":"grid","perRow":{{requested}},"reply":"ok"}""");

        outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Which.Payload.GetProperty("perRow").GetInt32().Should().Be(expected);
    }

    [TestMethod]
    [DataRow("""{"layout":"diagonal","reply":"x"}""")]
    [DataRow("""{"reply":"x"}""")]
    [DataRow("""{"layout":"grid","spacing":"tight","reply":"x"}""")]
    public async Task ExecuteAsync_RejectsUnknownLayoutOrSpacing(string args)
    {
        var outcome = await Run(args);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("invalid_arguments");
        outcome.Events.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task ExecuteAsync_WithFewerThanTwoNodes_HasNothingToArrange(int nodes)
    {
        var outcome = await Run("""{"layout":"horizontal","reply":"x"}""", Context(nodes));

        outcome.ModelResultJson.Should().Contain("nothing_to_arrange");
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutReply_LeavesTheFinalAnswerToTheModel()
    {
        var outcome = await Run("""{"layout":"horizontal"}""");

        outcome.FinalText.Should().BeNull();
    }
}
