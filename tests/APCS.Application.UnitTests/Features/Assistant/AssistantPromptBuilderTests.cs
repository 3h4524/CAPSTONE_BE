using APCS.Application.Abstractions.Ai;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace APCS.Application.UnitTests.Features.Assistant;

[TestClass]
public sealed class AssistantPromptBuilderTests
{
    private readonly AssistantPromptBuilder builder = new(new FakeTimeProvider(new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero)));

    [TestMethod]
    public void Build_StartsWithSystemPromptAndCallerRole()
    {
        var messages = builder.Build(AssistantTestSupport.Session(caller: AssistantTestSupport.Admin));

        messages[0].Role.Should().Be(AssistantMessageRole.System);
        messages[0].Text.Should().Contain("APCS Assistant").And.Contain("2026-10-29").And.Contain("The user is a Admin");
    }

    [TestMethod]
    public void Build_WithoutContext_OmitsAppContextBlock()
    {
        var messages = builder.Build(AssistantTestSupport.Session("hi"));

        messages[^1].Text.Should().Be("hi");
    }

    [TestMethod]
    public void Build_AppendsAppContextToLatestUserMessageOnly()
    {
        var context = new AssistantClientContextDto("/workflows?id=x", null, null, [], null);
        var messages = builder.Build(AssistantTestSupport.Session(
            "hi",
            context,
            history: [new(AssistantMessageRoles.User, "earlier"), new(AssistantMessageRoles.Assistant, "answer")]));

        messages[1].Text.Should().Be("earlier");
        messages[^1].Text.Should().StartWith("hi").And.Contain("<app_context>").And.Contain("/workflows?id=x");
        messages.Where(message => message.Role == AssistantMessageRole.User).Count(message => message.Text!.Contains("<app_context>")).Should().Be(1);
    }

    [TestMethod]
    public void Build_MapsHistoryRolesInOrder()
    {
        var messages = builder.Build(AssistantTestSupport.Session(
            "now",
            history: [new(AssistantMessageRoles.User, "q1"), new(AssistantMessageRoles.Assistant, "a1")]));

        messages.Select(message => message.Role).Should().Equal(
            AssistantMessageRole.System, AssistantMessageRole.User, AssistantMessageRole.Assistant, AssistantMessageRole.User);
    }

    [TestMethod]
    public void Build_AddsToolMemoryOnlyToTheLastThreeRememberedAssistantMessages()
    {
        var history = Enumerable.Range(1, 4)
            .SelectMany(i => new AssistantHistoryMessage[]
            {
                new(AssistantMessageRoles.User, $"q{i}"),
                new(AssistantMessageRoles.Assistant, $"a{i}", $"[{{\"tool\":\"list_workflows\",\"result\":\"r{i}\"}}]")
            })
            .ToArray();

        var messages = builder.Build(AssistantTestSupport.Session("now", history: history));

        var assistantTexts = messages.Where(message => message.Role == AssistantMessageRole.Assistant).Select(message => message.Text!).ToArray();
        assistantTexts[0].Should().Be("a1");
        assistantTexts[1..].Should().AllSatisfy(text => text.Should().Contain("<tool_memory>"));
        assistantTexts[3].Should().StartWith("a4").And.Contain("r4");
    }

    [TestMethod]
    public void Build_SystemPromptExplainsMessagesWithSeveralRequests() =>
        builder.Build(AssistantTestSupport.Session())[0].Text.Should().Contain("moreRequests").And.Contain("never drop a part");

    [TestMethod]
    public void Build_SystemPromptNoLongerTellsTheModelToStopAfterAnAction() =>
        builder.Build(AssistantTestSupport.Session())[0].Text.Should().NotContain("Nothing more is needed after the call");

    [TestMethod]
    public void Build_AddsActionMemoryToRememberedAssistantMessages()
    {
        var messages = builder.Build(AssistantTestSupport.Session(
            "it is still top to bottom",
            history:
            [
                new(AssistantMessageRoles.User, "tidy it"),
                new(AssistantMessageRoles.Assistant, "I arranged it.", null, """[{"action":"arrange_workflow_canvas","state":"applied","details":"{\"layout\":\"horizontal\"}"}]""")
            ]));

        var assistant = messages.Single(message => message.Role == AssistantMessageRole.Assistant).Text!;
        assistant.Should().StartWith("I arranged it.").And.Contain("<action_memory>").And.Contain("horizontal").And.Contain("applied");
        assistant.Should().NotContain("<tool_memory>");
    }

    [TestMethod]
    [DataRow("answer it first in plain text")]
    [DataRow("Never propose the same change again")]
    [DataRow("steer back to the open task")]
    [DataRow("first make the structure right")]
    [DataRow("<action_memory>")]
    [DataRow("Call navigate to /support only when the user asks")]
    [DataRow("describe the layout in words")]
    [DataRow("matches more than one step on the canvas")]
    [DataRow("write the query in English, then answer in the user's language")]
    [DataRow("Make the query stand alone")]
    [DataRow("Never say you will make a change unless you call the tool")]
    public void Build_SystemPromptHasConversationRules(string rule) =>
        builder.Build(AssistantTestSupport.Session())[0].Text.Should().Contain(rule);

    [TestMethod]
    public void Build_SystemPromptNeverMentionsRenaming() =>
        builder.Build(AssistantTestSupport.Session())[0].Text.Should().NotContainEquivalentOf("renam");

    [TestMethod]
    public void Build_SystemPromptExplainsToolMemory() =>
        builder.Build(AssistantTestSupport.Session())[0].Text.Should().Contain("<tool_memory>");

    [TestMethod]
    public void Build_ListsNodesInReadingOrderWithoutCoordinates()
    {
        var canvas = new AssistantCanvasDto(
            Guid.NewGuid(), 1, false,
            [
                new("c", "apply-mockup", "Apply mockup", AssistantTestSupport.Json("{}"), new(900, 10)),
                new("a", "product-input", "Input", AssistantTestSupport.Json("{}"), new(10, 20)),
                new("b", "design-image", "Design", AssistantTestSupport.Json("{}"), new(450, 0))
            ],
            []);
        var context = new AssistantClientContextDto("/workflows", canvas.WorkflowId, null, [], canvas);

        var text = builder.Build(AssistantTestSupport.Session("is it left to right?", context))[^1].Text!;

        text.IndexOf("\"id\":\"a\"", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("\"id\":\"b\"", StringComparison.Ordinal));
        text.IndexOf("\"id\":\"b\"", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("\"id\":\"c\"", StringComparison.Ordinal));
        text.Should().NotContain("\"x\"").And.NotContain("\"y\"");
    }

    [TestMethod]
    [DataRow(new double[] { 0, 0, 400, 30, 800, 10 }, "left to right in one row")]
    [DataRow(new double[] { 0, 0, 20, 300, 10, 600 }, "top to bottom in one column")]
    [DataRow(new double[] { 0, 0, 400, 0, 0, 300 }, "spread over several rows and columns")]
    public void Build_DescribesTheCurrentCanvasLayout(double[] coordinates, string expected)
    {
        var nodes = Enumerable.Range(0, coordinates.Length / 2)
            .Select(i => new AssistantCanvasNodeDto($"n{i}", "design-image", $"Step {i}", AssistantTestSupport.Json("{}"), new(coordinates[i * 2], coordinates[i * 2 + 1])))
            .ToArray();
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, nodes, []);

        var text = builder.Build(AssistantTestSupport.Session("how is it laid out?", new("/workflows", canvas.WorkflowId, null, [], canvas)))[^1].Text!;

        text.Should().Contain($"\"layout\":\"{expected}\"");
    }

    [TestMethod]
    public void Build_OmitsTheLayoutWithoutPositions()
    {
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, [new("a", "product-input", "Input", AssistantTestSupport.Json("{}"))], []);

        builder.Build(AssistantTestSupport.Session("hi", new("/workflows", canvas.WorkflowId, null, [], canvas)))[^1].Text.Should().NotContain("\"layout\"");
    }

    [TestMethod]
    public void Build_IncludesConfigOnlyForSelectedNodes()
    {
        var canvas = new AssistantCanvasDto(
            Guid.NewGuid(), 3, false,
            [
                new("a", "product-input", "Input", AssistantTestSupport.Json("""{"secretA":1}""")),
                new("b", "apply-mockup", "Mockup", AssistantTestSupport.Json("""{"secretB":2}"""))
            ],
            [new("e", "a", "b")]);
        var context = new AssistantClientContextDto("/workflows", canvas.WorkflowId, null, ["b"], canvas);

        var text = builder.Build(AssistantTestSupport.Session("explain", context))[^1].Text!;

        text.Should().Contain("secretB").And.NotContain("secretA");
        text.Should().Contain("product-input");
    }

    [TestMethod]
    public void Build_WithCanvas_AddsTheNodeCatalogToTheSystemPrompt()
    {
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, [], []);
        var context = new AssistantClientContextDto("/workflows", null, null, null, canvas);

        var system = builder.Build(AssistantTestSupport.Session("hi", context))[0].Text!;

        system.Should().Contain("<node_catalog>").And.Contain("generate-listing").And.Contain("connectionRules");
    }

    [TestMethod]
    public void Build_WithoutCanvasOrForAdmin_DoesNotAddTheNodeCatalog()
    {
        var canvas = new AssistantCanvasDto(Guid.NewGuid(), 1, false, [], []);
        var context = new AssistantClientContextDto("/workflows", null, null, null, canvas);

        builder.Build(AssistantTestSupport.Session("hi"))[0].Text.Should().NotContain("\"connectionRules\"");
        builder.Build(AssistantTestSupport.Session("hi", context, AssistantTestSupport.Admin))[0].Text.Should().NotContain("\"connectionRules\"");
    }

    [TestMethod]
    public void Build_WithArticles_AppendsThemToTheLatestUserMessageAndClipsLongContent()
    {
        var article = new APCS.Application.Features.Rag.Dtos.RagSearchResultDto(Guid.NewGuid(), Guid.NewGuid(), "Mockups", "faq", new string('c', 3000), null, .9, new Dictionary<string, object?>());

        var text = builder.Build(AssistantTestSupport.Session("how many"), [article])[^1].Text!;

        text.Should().Contain("<help_articles>").And.Contain("Mockups").And.NotContain(new string('c', 1300));
    }

    [TestMethod]
    public void Build_WithoutArticles_OmitsTheBlock() =>
        builder.Build(AssistantTestSupport.Session("hi"), [])[^1].Text.Should().Be("hi");

    [TestMethod]
    public void Build_TellsTheModelNeverToRevealToolNamesOrInternalDetails() =>
        builder.Build(AssistantTestSupport.Session("hi"))[0].Text.Should().Contain("Never mention tool names");

    [TestMethod]
    public void Build_TellsTheModelWhichWorkflowAndNodesTheUserMeans()
    {
        var canvas = new AssistantCanvasDto(
            Guid.NewGuid(), 1, false,
            [new("a", "design-image", "Design", AssistantTestSupport.Json("""{"variants":2}""")), new("b", "apply-mockup", "Mockup", AssistantTestSupport.Json("{}"))],
            [], "Spring flow");
        var context = new AssistantClientContextDto("/workflows", canvas.WorkflowId, null, ["a"], canvas);

        var messages = builder.Build(AssistantTestSupport.Session("what is this node", context));

        var user = messages[^1].Text!;
        user.Should().Contain("Spring flow").And.Contain("selectedNodes").And.Contain("design-image").And.Contain("variants");
        user.Should().NotContain("\"selectedNodeConfigs\"");
        messages[0].Text.Should().Contain("Never ask which workflow or node the user means").And.Contain("Be decisive");
    }
}
