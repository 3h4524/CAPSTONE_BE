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
}
