using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Persistence;
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
    private readonly List<AssistantMessage> saved = [];

    public AssistantAgentLoopTests()
    {
        messages.Setup(r => r.AddAsync(It.IsAny<AssistantMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantMessage, bool, CancellationToken>((message, _, _) => saved.Add(message))
            .Returns(Task.CompletedTask);
        conversations.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantConversation());
    }

    private AssistantAgentLoop Loop(FakeChatModel model, params IAssistantTool[] tools) => new(
        model,
        new AssistantPromptBuilder(new FakeTimeProvider()),
        new AssistantToolRegistry(tools),
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

        events.Select(e => e.Type).Should().Equal("conversation", "delta", "delta", "done");
        saved.Should().ContainSingle().Which.Should().Match<AssistantMessage>(m =>
            m.Content == "Hello" && m.Role == "assistant" && m.InputTokens == 10 && m.OutputTokens == 5);
        unitOfWork.Verify(u => u.SaveChangesAsync(CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_QueryTool_ExecutesToolAndCallsModelAgainWithResult()
    {
        var tool = new StubTool("lookup", execute: (_, _) => Task.FromResult(AssistantToolOutcome.Ok(new { value = 7 })));
        var model = new FakeChatModel(
            [AssistantTestSupport.Done("", AssistantTestSupport.Call("lookup", callId: "c1"))],
            [AssistantTestSupport.Text("It is 7"), AssistantTestSupport.Done()]);

        var events = await Collect(Loop(model, tool).RunAsync(AssistantTestSupport.Session()));

        events.Select(e => e.Type).Should().Equal("conversation", "tool_started", "tool_finished", "delta", "done");
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
        events.OfType<AssistantStreamEvent>().Single(e => e.Type == "tool_finished").Data
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
}
