using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Tools;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant;

[TestClass]
public sealed class AssistantAgentLoopTests
{
    private readonly Mock<IRepository<AssistantConversation>> conversations = new();
    private readonly Mock<IRepository<AssistantMessage>> messages = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IRagService> rag = new();
    private readonly Mock<ICacheService> cache = new();
    private readonly List<AssistantMessage> saved = [];

    public AssistantAgentLoopTests()
    {
        messages.Setup(r => r.AddAsync(It.IsAny<AssistantMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantMessage, bool, CancellationToken>((message, _, _) => saved.Add(message))
            .Returns(Task.CompletedTask);
        conversations.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantConversation());
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private AssistantAgentLoop Loop(FakeChatModel model, params IAssistantTool[] tools) => new(
        model,
        new AssistantPromptBuilder(new FakeTimeProvider()),
        new AssistantToolRegistry(tools),
        new AssistantKnowledgeBase(rag.Object, cache.Object, NullLogger<AssistantKnowledgeBase>.Instance),
        conversations.Object,
        messages.Object,
        unitOfWork.Object,
        new FakeTimeProvider(),
        NullLogger<AssistantAgentLoop>.Instance);

    private static async Task<List<AssistantStreamEvent>> Collect(IAsyncEnumerable<AssistantStreamEvent> events)
    {
        var list = new List<AssistantStreamEvent>();
        await foreach (var item in events)
            list.Add(item);
        return list;
    }

    [TestMethod]
    public async Task RunAsync_TextOnly_EmitsConversationDeltasAndDone()
    {
        var model = new FakeChatModel([AssistantTestSupport.Text("Hel"), AssistantTestSupport.Text("lo"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(AssistantTestSupport.Session()));

        events.Select(e => e.Type).Should().Equal("conversation", "tool_started", "tool_finished", "delta", "delta", "done");
        saved.Should().ContainSingle().Which.Should().Match<AssistantMessage>(m =>
            m.Content == "Hello" && m.Role == "assistant" && m.InputTokens == 10 && m.OutputTokens == 5);
        unitOfWork.Verify(u => u.SaveChangesAsync(CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_ModelRetrying_EmitsStatusBeforeTheAnswer()
    {
        var model = new FakeChatModel([AssistantModelUpdate.Retrying(8), AssistantTestSupport.Text("Hi"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(AssistantTestSupport.Session()));

        events.Select(e => e.Type).Should().Equal("conversation", "tool_started", "tool_finished", "status", "delta", "done");
        events.Single(e => e.Type == "status").Data.Should().BeEquivalentTo(new AssistantStatusEventData("retrying", 8));
    }

    [TestMethod]
    public async Task RunAsync_QueryTool_RemembersItsResultInMetadata()
    {
        var tool = new StubTool("list_workflows", execute: (_, _) => Task.FromResult(AssistantToolOutcome.Ok(new { items = new[] { new { id = "w1" } } })));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("list_workflows", callId: "c1"))],
            [AssistantTestSupport.Text("You have one"), AssistantTestSupport.Done()]);

        await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        var metadata = AssistantJson.Deserialize<AssistantMessageMetadata>(saved.Single().Metadata)!;
        metadata.ToolMemory.Should().ContainSingle().Which.Should().Match<AssistantToolMemoryData>(memory =>
            memory.Tool == "list_workflows" && memory.Result.Contains("w1"));
    }

    [TestMethod]
    public async Task RunAsync_NonQueryToolsAndSearchFaq_AreNotRemembered()
    {
        var faq = new StubTool("search_faq");
        var navigate = new StubTool("navigate", AssistantToolKind.ClientAction);
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("search_faq", callId: "c1"), AssistantTestSupport.Call("navigate", callId: "c2"))],
            [AssistantTestSupport.Text("Done"), AssistantTestSupport.Done()]);

        await Collect(Loop(model, faq, navigate).RunAsync(AssistantTestSupport.Session()));

        AssistantJson.Deserialize<AssistantMessageMetadata>(saved.Single().Metadata)!.ToolMemory.Should().BeNull();
    }

    [TestMethod]
    public async Task RunAsync_LargeToolResults_AreTruncatedAndCapped()
    {
        var big = new string('x', 5000);
        var tool = new StubTool("get_workflow", execute: (_, _) => Task.FromResult(AssistantToolOutcome.Ok(new { big })));
        var calls = Enumerable.Range(1, 5).Select(i => AssistantTestSupport.Call("get_workflow", callId: $"c{i}")).ToArray();
        var model = new FakeChatModel([AssistantTestSupport.Done("", calls)], [AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()]);

        await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        var memory = AssistantJson.Deserialize<AssistantMessageMetadata>(saved.Single().Metadata)!.ToolMemory!;
        memory.Should().OnlyContain(item => item.Result.Length < 1000);
        memory.Sum(item => item.Result.Length).Should().BeLessThanOrEqualTo(AssistantLimits.ToolMemoryMessageChars);
    }

    private static StubTool ActionTool(string name, string reply) => new(
        name,
        AssistantToolKind.ClientAction,
        (_, _) => Task.FromResult(AssistantToolOutcome.Ok(
            new { status = "offered" },
            true,
            AssistantStreamEvent.ClientAction(new AssistantClientActionData(Guid.NewGuid(), name, AssistantTestSupport.Json("{}"))))
            .WithFinalText(reply)));

    [TestMethod]
    public async Task RunAsync_ActionWithMoreRequests_ContinuesToAnswerTheRestOfTheMessage()
    {
        var edit = ActionTool("edit_workflow_canvas", "I added the approval step.");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("edit_workflow_canvas", """{"moreRequests":true}""", "c1"))],
            [AssistantTestSupport.Text("APCS turns products into listings."), AssistantTestSupport.Done()]);

        await Collect(Loop(model, edit).RunAsync(AssistantTestSupport.Session()));

        model.Requests.Should().HaveCount(2);
        model.Requests[1].AllowTools.Should().BeTrue();
        saved.Single().Content.Should().Be("I added the approval step.\n\nAPCS turns products into listings.");
    }

    [TestMethod]
    public async Task RunAsync_ActionWithMoreRequests_CanCallASecondAction()
    {
        var edit = ActionTool("edit_workflow_canvas", "I connected the steps.");
        var arrange = ActionTool("arrange_workflow_canvas", "I tidied the layout.");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("edit_workflow_canvas", """{"moreRequests":true}""", "c1"))],
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("arrange_workflow_canvas", "{}", "c2"))]);

        var events = await Collect(Loop(model, edit, arrange).RunAsync(AssistantTestSupport.Session()));

        model.Requests.Should().HaveCount(2);
        events.Count(e => e.Type == "client_action").Should().Be(2);
        saved.Single().Content.Should().Be("I connected the steps.\n\nI tidied the layout.");
    }

    [TestMethod]
    public async Task RunAsync_TwoActionsInOneResponse_ShowBothReplies()
    {
        var edit = ActionTool("edit_workflow_canvas", "I connected the steps.");
        var arrange = ActionTool("arrange_workflow_canvas", "I tidied the layout.");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("edit_workflow_canvas", "{}", "c1"), AssistantTestSupport.Call("arrange_workflow_canvas", "{}", "c2"))]);

        await Collect(Loop(model, edit, arrange).RunAsync(AssistantTestSupport.Session()));

        model.Requests.Should().ContainSingle();
        saved.Single().Content.Should().Be("I connected the steps.\n\nI tidied the layout.");
    }

    [TestMethod]
    public async Task RunAsync_ActionWithoutMoreRequests_EndsTheTurnWithoutAnotherModelCall()
    {
        var edit = ActionTool("edit_workflow_canvas", "I added the step.");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("edit_workflow_canvas", """{"moreRequests":false}""", "c1"))],
            [AssistantTestSupport.Text("should not run"), AssistantTestSupport.Done()]);

        await Collect(Loop(model, edit).RunAsync(AssistantTestSupport.Session()));

        model.Requests.Should().ContainSingle();
        saved.Single().Content.Should().Be("I added the step.");
    }

    [TestMethod]
    public async Task RunAsync_FaqQuestionAndAction_KeepsBothTheAnswerAndTheAction()
    {
        var faq = new StubTool("search_faq", execute: (_, _) => Task.FromResult(AssistantToolOutcome.Ok(new { results = new[] { new { title = "About APCS" } } })));
        var edit = ActionTool("edit_workflow_canvas", "I added the approval step.");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("search_faq", """{"query":"what is APCS"}""", "c1"))],
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("edit_workflow_canvas", """{"moreRequests":true}""", "c2"))],
            [AssistantTestSupport.Text("APCS is a print on demand assistant."), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, faq, edit).RunAsync(AssistantTestSupport.Session("what is APCS and add an approval step")));

        events.Should().Contain(e => e.Type == "client_action");
        saved.Single().Content.Should().Contain("I added the approval step.").And.Contain("APCS is a print on demand assistant.");
    }

    [TestMethod]
    public async Task RunAsync_TextAcrossToolRounds_IsSeparatedIntoParagraphs()
    {
        var tool = new StubTool("lookup");
        var model = new FakeChatModel(
            [AssistantTestSupport.Text("Let me check."), AssistantTestSupport.Done("Let me check.", AssistantTestSupport.Call("lookup", callId: "c1"))],
            [AssistantTestSupport.Text("It is 7."), AssistantTestSupport.Done()]);

        await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        saved.Single().Content.Should().Be("Let me check.\n\nIt is 7.");
    }

    [TestMethod]
    public async Task RunAsync_QueryTool_ExecutesToolAndCallsModelAgainWithResult()
    {
        var tool = new StubTool("lookup", execute: (_, _) => Task.FromResult(AssistantToolOutcome.Ok(new { value = 7 })));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("lookup", callId: "c1"))],
            [AssistantTestSupport.Text("It is 7"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        events.Select(e => e.Type).Should().Equal("conversation", "tool_started", "tool_finished", "tool_started", "tool_finished", "delta", "done");
        model.Requests.Should().HaveCount(2);
        model.Requests[1].Messages.Last().ToolResult.Should().Match<AssistantToolResult>(r => r.CallId == "c1" && r.Json.Contains("7"));
        model.Requests[1].AllowTools.Should().BeTrue();
    }

    [TestMethod]
    public async Task RunAsync_SearchFaq_EmitsCitationsAndStoresThemInMetadata()
    {
        var citation = new AssistantCitationData(Guid.NewGuid(), "Mockups", null);
        var tool = new StubTool("search_faq", execute: (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { results = 1 }, false, AssistantStreamEvent.Citations([citation]))));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("search_faq"))],
            [AssistantTestSupport.Text("8"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        events.Should().Contain(e => e.Type == "citations");
        saved.Single().Metadata.Should().Contain("Mockups").And.Contain("search_faq");
    }

    [TestMethod]
    public async Task RunAsync_ConfirmationTool_EndsTurnWithFinalPassWithoutTools()
    {
        var pendingId = Guid.NewGuid();
        var pending = new AssistantPendingActionEventData(pendingId, "start_workflow_run", "Run", "Starts", "pending", DateTime.UtcNow);
        var tool = new StubTool("start_workflow_run", AssistantToolKind.Confirmation, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "awaiting_confirmation" }, true, AssistantStreamEvent.PendingAction(pending))));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("start_workflow_run"))],
            [AssistantTestSupport.Text("Press Confirm"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        events.Should().Contain(e => e.Type == "pending_action");
        model.Requests.Should().HaveCount(2);
        model.Requests[1].AllowTools.Should().BeFalse();
        saved.Single().Metadata.Should().Contain(pendingId.ToString());
    }

    [TestMethod]
    public async Task RunAsync_ClientActionTool_EmitsClientActionAndEndsTurn()
    {
        var action = new AssistantClientActionData(Guid.NewGuid(), "navigate", AssistantTestSupport.Json("""{"path":"/usage"}"""));
        var tool = new StubTool("navigate", AssistantToolKind.ClientAction, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "offered" }, true, AssistantStreamEvent.ClientAction(action))));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("navigate"))],
            [AssistantTestSupport.Text("Here"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        events.Should().Contain(e => e.Type == "client_action");
        model.Requests[1].AllowTools.Should().BeFalse();
        saved.Single().Metadata.Should().Contain("navigate");
    }

    [TestMethod]
    public async Task RunAsync_OfferChoices_EmitsChoicesAndStoresThem()
    {
        var choices = new AssistantChoicesData("Which?", [new("Standard", "standard"), new("AI", "ai")], true);
        var tool = new StubTool("offer_choices", AssistantToolKind.Interaction, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "shown" }, true, AssistantStreamEvent.Choices(choices))));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("offer_choices"))],
            [AssistantTestSupport.Text("Pick one"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        events.Should().Contain(e => e.Type == "choices");
        saved.Single().Metadata.Should().Contain("Which?").And.Contain("standard");
    }

    [TestMethod]
    public async Task RunAsync_UnknownTool_ReturnsErrorResultToModelAndContinues()
    {
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("ghost"))],
            [AssistantTestSupport.Text("Sorry"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(AssistantTestSupport.Session()));

        model.Requests[1].Messages.Last().ToolResult!.Json.Should().Contain("unknown_tool");
        events.Last(e => e.Type == "tool_finished").Data
            .Should().Match<AssistantToolFinishedEventData>(d => !d.Ok);
    }

    [TestMethod]
    public async Task RunAsync_ToolThrows_ReturnsToolFailedResultAndContinues()
    {
        var tool = new StubTool("boom", execute: (_, _) => throw new InvalidOperationException("secret"));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("boom"))],
            [AssistantTestSupport.Text("Failed"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        var result = model.Requests[1].Messages.Last().ToolResult!.Json;
        result.Should().Contain("tool_failed").And.NotContain("secret");
        events.Last().Type.Should().Be("done");
    }

    [TestMethod]
    public async Task RunAsync_ModelFails_EmitsErrorWithoutDoneAndStoresErrorMetadata()
    {
        var model = new FakeChatModel([AssistantModelUpdate.Failed("model_unavailable")]);

        var events = await Collect(Loop(model).RunAsync(AssistantTestSupport.Session()));

        events.Last().Type.Should().Be("error");
        events.Should().NotContain(e => e.Type == "done");
        saved.Should().ContainSingle().Which.Metadata.Should().Contain(AssistantErrors.ModelUnavailableCode);
    }

    [TestMethod]
    public async Task RunAsync_ModelNotConfigured_EmitsModelNotConfiguredError()
    {
        var model = new FakeChatModel([AssistantModelUpdate.Failed("not_configured")]);

        var events = await Collect(Loop(model).RunAsync(AssistantTestSupport.Session()));

        events.Last().Data.Should().Match<AssistantErrorData>(e => e.Code == AssistantErrors.ModelNotConfiguredCode);
    }

    [TestMethod]
    public async Task RunAsync_ToolCallsEveryIteration_StopsAfterMaxIterations()
    {
        var tool = new StubTool("again");
        var turns = Enumerable.Range(0, 10)
            .Select(_ => (IReadOnlyList<AssistantModelUpdate>)[AssistantTestSupport.Done("", AssistantTestSupport.Call("again"))])
            .ToArray();
        var model = new FakeChatModel(turns);

        await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        model.Requests.Should().HaveCount(AssistantLimits.MaxToolIterations + 1);
        model.Requests.Last().AllowTools.Should().BeFalse();
    }

    [TestMethod]
    public async Task RunAsync_UnavailableToolsAreNotDeclared()
    {
        var model = new FakeChatModel([AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()]);
        var loop = Loop(model,
            new StubTool("search_faq"),
            new StubTool("list_workflows", available: context => context.Caller.IsSeller));

        await Collect(loop.RunAsync(AssistantTestSupport.Session(caller: AssistantTestSupport.Admin)));

        model.Requests.Single().Tools.Select(tool => tool.Name).Should().Equal("search_faq");
    }

    [TestMethod]
    public async Task RunAsync_Cancelled_SavesPartialTextWithNoneToken()
    {
        using var cancellation = new CancellationTokenSource();
        var model = new FakeChatModel([AssistantTestSupport.Text("partial"), AssistantTestSupport.Text("more"), AssistantTestSupport.Done()]);

        var act = async () =>
        {
            await foreach (var item in Loop(model).RunAsync(AssistantTestSupport.Session(), cancellation.Token))
            {
                if (item.Type == "delta")
                    cancellation.Cancel();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        saved.Should().ContainSingle().Which.Content.Should().Be("partial");
        unitOfWork.Verify(u => u.SaveChangesAsync(CancellationToken.None), Times.Once);
    }

    private static RagSearchResultDto Article(string title = "Mockups") =>
        new(Guid.NewGuid(), Guid.NewGuid(), title, "faq", "Up to 8 mockups.", null, .9, new Dictionary<string, object?>());

    private static AssistantChatSession FirstMessage(string message = "How many mockups?") =>
        AssistantTestSupport.Session(message);

    [TestMethod]
    public async Task RunAsync_PrefetchesHelpArticlesWithTheUsersOwnMessageAndGivesThemToTheModel()
    {
        rag.Setup(r => r.SearchAsync(It.Is<RagSearchRequest>(q => q.Query == "How many mockups?" && q.ContentType == "faq"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Article()]);
        var model = new FakeChatModel([AssistantTestSupport.Text("8"), AssistantTestSupport.Done()]);

        await Collect(Loop(model).RunAsync(FirstMessage()));

        model.Requests.Single().Messages.Last().Text.Should().Contain("<help_articles>").And.Contain("Up to 8 mockups.");
        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_WhenPrefetchFails_StillAnswers()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("429"));
        var model = new FakeChatModel([AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(FirstMessage()));

        events.Last().Type.Should().Be("done");
        model.Requests.Single().Messages.Last().Text.Should().NotContain("<help_articles>");
    }

    [TestMethod]
    public async Task RunAsync_AnswerFromHelpArticlesOnly_EmitsCitations()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Article("Mockups"), Article("Videos")]);
        var model = new FakeChatModel([AssistantTestSupport.Text("8"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(FirstMessage()));

        events.Single(e => e.Type == "citations").Data.Should().BeOfType<AssistantCitationsEventData>().Which.Items.Select(i => i.Title).Should().Equal("Mockups", "Videos");
        saved.Single().Metadata.Should().Contain("Mockups");
    }

    [TestMethod]
    public async Task RunAsync_WhenAnotherToolWasUsed_DoesNotShowThePrefetchedArticlesAsSources()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Article()]);
        var tool = new StubTool("list_workflows");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("list_workflows"))],
            [AssistantTestSupport.Text("You have 2"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(FirstMessage("list my workflows")));

        events.Should().NotContain(e => e.Type == "citations");
    }

    [TestMethod]
    public async Task RunAsync_EndTurnToolAfterExplanatoryText_SkipsTheFinalModelCall()
    {
        var action = new AssistantClientActionData(Guid.NewGuid(), "navigate", AssistantTestSupport.Json("""{"path":"/usage"}"""));
        var tool = new StubTool("navigate", AssistantToolKind.ClientAction, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "offered" }, true, AssistantStreamEvent.ClientAction(action))));
        var model = new FakeChatModel(
            [AssistantTestSupport.Text("Here is the link."), AssistantTestSupport.Done("Here is the link.", AssistantTestSupport.Call("navigate"))]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session("open usage", new(("/dashboard"), null, null, null, null))));

        model.Requests.Should().HaveCount(1);
        events.Should().Contain(e => e.Type == "client_action");
        saved.Single().Content.Should().Be("Here is the link.");
    }

    [TestMethod]
    public async Task RunAsync_EndTurnToolWithoutText_StillMakesTheFinalCall()
    {
        var tool = new StubTool("offer_choices", AssistantToolKind.Interaction, (_, _) => Task.FromResult(AssistantToolOutcome.Ok(new { status = "shown" }, true)));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("offer_choices"))],
            [AssistantTestSupport.Text("Pick one"), AssistantTestSupport.Done()]);

        await Collect(Loop(model, tool).RunAsync(FirstMessage()));

        model.Requests.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task RunAsync_CachedAnswer_ReplaysWithoutCallingTheModelOrSearching()
    {
        cache.Setup(c => c.GetAsync<string>(APCS.Application.Features.Rag.Common.RagCacheConstants.SearchGenerationKey, It.IsAny<CancellationToken>())).ReturnsAsync("gen1");
        cache.Setup(c => c.GetAsync<AssistantCachedAnswer>(It.Is<string>(k => k.StartsWith("assistant:answer:gen1:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantCachedAnswer(new string('a', 130), [new AssistantCitationData(Guid.NewGuid(), "Mockups", null)]));
        var model = new FakeChatModel();

        var events = await Collect(Loop(model).RunAsync(FirstMessage()));

        model.Requests.Should().BeEmpty();
        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        events.Count(e => e.Type == "delta").Should().Be(3);
        events.Should().Contain(e => e.Type == "citations");
        events.Last().Data.Should().BeOfType<AssistantDoneEventData>().Which.Model.Should().Be("cache");
        saved.Single().Content.Should().HaveLength(130);
    }

    [TestMethod]
    public async Task RunAsync_FreshKnowledgeAnswer_IsSavedToTheAnswerCache()
    {
        cache.Setup(c => c.GetAsync<string>(APCS.Application.Features.Rag.Common.RagCacheConstants.SearchGenerationKey, It.IsAny<CancellationToken>())).ReturnsAsync("gen1");
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([Article()]);
        var model = new FakeChatModel([AssistantTestSupport.Text("8 mockups"), AssistantTestSupport.Done()]);

        await Collect(Loop(model).RunAsync(FirstMessage()));

        cache.Verify(c => c.SetAsync(It.Is<string>(k => k.StartsWith("assistant:answer:gen1:")), It.Is<AssistantCachedAnswer>(a => a.Text == "8 mockups" && a.Citations.Count == 1), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_AnswersThatUsedUserDataToolsAreNeverCached()
    {
        cache.Setup(c => c.GetAsync<string>(APCS.Application.Features.Rag.Common.RagCacheConstants.SearchGenerationKey, It.IsAny<CancellationToken>())).ReturnsAsync("gen1");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("list_workflows"))],
            [AssistantTestSupport.Text("You have 2"), AssistantTestSupport.Done()]);

        await Collect(Loop(model, new StubTool("list_workflows")).RunAsync(FirstMessage("list my workflows")));

        cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AssistantCachedAnswer>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RunAsync_FollowUpMessagesAndCanvasQuestionsAreNotEligibleForTheAnswerCache()
    {
        cache.Setup(c => c.GetAsync<string>(APCS.Application.Features.Rag.Common.RagCacheConstants.SearchGenerationKey, It.IsAny<CancellationToken>())).ReturnsAsync("gen1");
        var followUp = AssistantTestSupport.Session("and then?", history: [new(AssistantMessageRoles.User, "hi"), new(AssistantMessageRoles.Assistant, "hello")]);
        var canvas = new APCS.Application.Features.Assistant.Dtos.Request.AssistantCanvasDto(Guid.NewGuid(), 1, false, [], []);
        var withCanvas = AssistantTestSupport.Session("explain this", new("/workflows", null, null, null, canvas));

        await Collect(Loop(new FakeChatModel([AssistantTestSupport.Text("x"), AssistantTestSupport.Done()])).RunAsync(followUp));
        await Collect(Loop(new FakeChatModel([AssistantTestSupport.Text("y"), AssistantTestSupport.Done()])).RunAsync(withCanvas));

        cache.Verify(c => c.GetAsync<AssistantCachedAnswer>(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AssistantCachedAnswer>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AnswerCacheKey_DependsOnThePagePathButNotItsQuery()
    {
        cache.Setup(c => c.GetAsync<string>(APCS.Application.Features.Rag.Common.RagCacheConstants.SearchGenerationKey, It.IsAny<CancellationToken>())).ReturnsAsync("gen1");
        var keys = new List<string>();
        cache.Setup(c => c.GetAsync<AssistantCachedAnswer>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => keys.Add(key))
            .ReturnsAsync((AssistantCachedAnswer?)null);
        var knowledgeBase = new AssistantKnowledgeBase(rag.Object, cache.Object, NullLogger<AssistantKnowledgeBase>.Instance);

        foreach (var route in new[] { "/workflows", "/batches", "/workflows?id=1", "/workflows?id=2" })
            await knowledgeBase.GetAnswerAsync(AssistantTestSupport.Session("how do I use this?", new(route, null, null, null, null)), CancellationToken.None);

        keys.Should().HaveCount(4);
        keys[0].Should().NotBe(keys[1]);
        keys[2].Should().Be(keys[0]).And.Be(keys[3]);
    }

    [TestMethod]
    public async Task RunAsync_WhenTheAnswerCacheIsDown_FallsBackToTheModel()
    {
        cache.Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
        var model = new FakeChatModel([AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(FirstMessage()));

        events.Last().Type.Should().Be("done");
        model.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task RunAsync_ToolFinalText_IsStreamedAsTheAnswerAndSkipsTheFinalModelCall()
    {
        var tool = new StubTool("edit_workflow_canvas", AssistantToolKind.ClientAction, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "proposed" }, true).WithFinalText("Review and apply it.")));
        var model = new FakeChatModel([AssistantTestSupport.Done("", AssistantTestSupport.Call("edit_workflow_canvas"))]);

        var events = await Collect(Loop(model, tool).RunAsync(FirstMessage("add a step")));

        model.Requests.Should().HaveCount(1);
        events.Where(e => e.Type == "delta").Select(e => ((AssistantDeltaEventData)e.Data).Text).Should().Equal("Review and apply it.");
        saved.Single().Content.Should().Be("Review and apply it.");
    }

    [TestMethod]
    public async Task RunAsync_EmptyToolFinalText_SkipsTheFinalCallWithoutAddingText()
    {
        var tool = new StubTool("offer_choices", AssistantToolKind.Interaction, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "shown" }, true).WithFinalText(string.Empty)));
        var model = new FakeChatModel([AssistantTestSupport.Done("", AssistantTestSupport.Call("offer_choices"))]);

        var events = await Collect(Loop(model, tool).RunAsync(FirstMessage("create a video")));

        model.Requests.Should().HaveCount(1);
        events.Should().NotContain(e => e.Type == "delta");
    }

    [TestMethod]
    public async Task RunAsync_ModelTextWithTheCall_IsNotDuplicatedByTheToolReply()
    {
        var tool = new StubTool("navigate", AssistantToolKind.ClientAction, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "offered" }, true).WithFinalText("Tool reply.")));
        var model = new FakeChatModel([AssistantTestSupport.Text("Model text."), AssistantTestSupport.Done("Model text.", AssistantTestSupport.Call("navigate"))]);

        await Collect(Loop(model, tool).RunAsync(FirstMessage("open usage")));

        saved.Single().Content.Should().Be("Model text.");
    }

    [TestMethod]
    public async Task RunAsync_WhenThePrefetchIsTooSlow_ContinuesWithoutArticles()
    {
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()))
            .Returns<RagSearchRequest, CancellationToken>(async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return [];
            });
        var model = new FakeChatModel([AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()]);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var events = await Collect(Loop(model).RunAsync(FirstMessage()));

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        events.Last().Type.Should().Be("done");
        model.Requests.Single().Messages.Last().Text.Should().NotContain("<help_articles>");
    }

    [TestMethod]
    public async Task RunAsync_WhenTheUserHasAWorkflowCanvasOpen_SkipsThePrefetch()
    {
        var canvas = new APCS.Application.Features.Assistant.Dtos.Request.AssistantCanvasDto(Guid.NewGuid(), 1, false, [], []);
        var model = new FakeChatModel([AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()]);

        await Collect(Loop(model).RunAsync(AssistantTestSupport.Session("add a step", new("/workflows", null, null, null, canvas))));

        rag.Verify(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RunAsync_WhenThePrefetchIsSkipped_ShowsNoHelpArticleStatus()
    {
        var canvas = new APCS.Application.Features.Assistant.Dtos.Request.AssistantCanvasDto(Guid.NewGuid(), 1, false, [], []);
        var model = new FakeChatModel([AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(AssistantTestSupport.Session("add a step", new("/workflows", null, null, null, canvas))));

        events.Select(e => e.Type).Should().Equal("conversation", "delta", "done");
    }

    [TestMethod]
    public async Task RunAsync_WhenTheModelAnswersNothing_RetriesOnceAndUsesTheSecondAnswer()
    {
        var model = new FakeChatModel([AssistantTestSupport.Done()], [AssistantTestSupport.Text("Here you go"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(FirstMessage()));

        model.Requests.Should().HaveCount(2);
        events.Last().Type.Should().Be("done");
        saved.Single().Content.Should().Be("Here you go");
    }

    [TestMethod]
    public async Task RunAsync_WhenTheModelStaysSilent_ReportsAnErrorInsteadOfAnEmptyDone()
    {
        var model = new FakeChatModel([AssistantTestSupport.Done()], [AssistantTestSupport.Done()], [AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model).RunAsync(FirstMessage()));

        model.Requests.Should().HaveCount(2);
        events.Should().NotContain(e => e.Type == "done");
        events.Last().Data.Should().BeOfType<AssistantErrorData>().Which.Code.Should().Be(AssistantErrors.EmptyResponseCode);
        saved.Single().Metadata.Should().Contain(AssistantErrors.EmptyResponseCode);
    }

    [TestMethod]
    public async Task RunAsync_WhenTheFinalAnswerAfterToolsIsEmpty_StillReportsAnError()
    {
        var tool = new StubTool("list_workflows");
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("list_workflows"))],
            [AssistantTestSupport.Done()],
            [AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(FirstMessage("list my workflows")));

        events.Last().Type.Should().Be("error");
        events.Should().NotContain(e => e.Type == "done");
    }

    [TestMethod]
    public async Task RunAsync_AnActionWithoutAnyText_CountsAsAnAnswer()
    {
        var action = new AssistantClientActionData(Guid.NewGuid(), "navigate", AssistantTestSupport.Json("""{"path":"/usage"}"""));
        var tool = new StubTool("navigate", AssistantToolKind.ClientAction, (_, _) => Task.FromResult(
            AssistantToolOutcome.Ok(new { status = "offered" }, true, AssistantStreamEvent.ClientAction(action)).WithFinalText(string.Empty)));
        var model = new FakeChatModel([AssistantTestSupport.Done("", AssistantTestSupport.Call("navigate"))]);

        var events = await Collect(Loop(model, tool).RunAsync(FirstMessage("open usage")));

        events.Last().Type.Should().Be("done");
        events.Should().NotContain(e => e.Type == "error");
    }
}
