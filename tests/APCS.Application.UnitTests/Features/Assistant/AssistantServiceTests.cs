using System.Linq.Expressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant;

[TestClass]
public sealed class AssistantServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> currentUser = new();
    private readonly Mock<IRepository<AssistantConversation>> conversations = new();
    private readonly Mock<IRepository<AssistantMessage>> messages = new();
    private readonly Mock<IAssistantPendingActionRepository> pendingActions = new();
    private readonly Mock<IAssistantConversationRepository> conversationStore = new();
    private readonly Mock<IAssistantConversationPurgeQueue> purgeQueue = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly FakeTimeProvider time = new(Now);
    private readonly List<AssistantMessage> addedMessages = [];
    private readonly List<AssistantConversation> addedConversations = [];
    private readonly Mock<IAssistantConfirmableTool> confirmTool = new();

    public AssistantServiceTests()
    {
        currentUser.SetupGet(u => u.UserId).Returns(AssistantTestSupport.UserId);
        messages.Setup(r => r.AddAsync(It.IsAny<AssistantMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantMessage, bool, CancellationToken>((m, _, _) => addedMessages.Add(m)).Returns(Task.CompletedTask);
        conversations.Setup(r => r.AddAsync(It.IsAny<AssistantConversation>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantConversation, bool, CancellationToken>((c, _, _) => addedConversations.Add(c)).Returns(Task.CompletedTask);
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation>().AsQueryable().BuildMock());
        messages.Setup(r => r.Query()).Returns(new List<AssistantMessage>().AsQueryable().BuildMock());
        pendingActions.Setup(r => r.Query()).Returns(new List<AssistantPendingAction>().AsQueryable().BuildMock());
        confirmTool.SetupGet(t => t.Name).Returns("start_workflow_run");
    }

    private AssistantService Service()
    {
        var model = new FakeChatModel();
        var loop = new AssistantAgentLoop(
            model, new AssistantPromptBuilder(time), new AssistantToolRegistry([]),
            new AssistantKnowledgeBase(new Mock<APCS.Application.Abstractions.Rag.IRagService>().Object, new Mock<APCS.Application.Abstractions.Caching.ICacheService>().Object, NullLogger<AssistantKnowledgeBase>.Instance),
            conversations.Object, messages.Object, unitOfWork.Object, time, NullLogger<AssistantAgentLoop>.Instance);
        return new AssistantService(
            currentUser.Object, conversations.Object, messages.Object, pendingActions.Object, conversationStore.Object, purgeQueue.Object, unitOfWork.Object,
            new SendAssistantMessageValidator(), loop, new AssistantToolRegistry([confirmTool.Object]), time);
    }

    private static AssistantConversation Conversation(Guid? userId = null, DateTime? deletedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId ?? AssistantTestSupport.UserId,
        Title = "Existing",
        UpdatedAt = Now.UtcDateTime,
        DeletedAt = deletedAt
    };

    private static AssistantPendingAction Action(string status = "pending", Guid? userId = null, DateTime? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        UserId = userId ?? AssistantTestSupport.UserId,
        ToolName = "start_workflow_run",
        Arguments = """{"workflowId":"abc"}""",
        Title = "Run workflow",
        Summary = "Starts a run",
        Status = status,
        ExpiresAt = expiresAt ?? Now.UtcDateTime.AddMinutes(10),
        CreatedAt = Now.UtcDateTime
    };

    private void ReturnAction(AssistantPendingAction? action) =>
        pendingActions.Setup(r => r.FindAsync(It.IsAny<Expression<Func<AssistantPendingAction, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(action is null ? [] : [action]);

    [TestMethod]
    public async Task StartChatAsync_WhenMessageEmpty_ReturnsValidationError()
    {
        var result = await Service().StartChatAsync(new(null, "  ", null), AssistantTestSupport.Seller);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        messages.Verify(r => r.AddAsync(It.IsAny<AssistantMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartChatAsync_WhenMessageTooLong_ReturnsValidationError()
    {
        var result = await Service().StartChatAsync(new(null, new string('a', 4001), null), AssistantTestSupport.Seller);

        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task StartChatAsync_WhenUnauthenticated_ReturnsUnauthorized()
    {
        currentUser.SetupGet(u => u.UserId).Returns((Guid?)null);

        var result = await Service().StartChatAsync(new(null, "hi", null), AssistantTestSupport.Seller);

        result.Error.Code.Should().Be("assistant.unauthorized");
    }

    [TestMethod]
    public async Task StartChatAsync_WithoutConversationId_CreatesConversationWithTruncatedTitle()
    {
        using var cancellation = new CancellationTokenSource();
        var text = new string('x', 100);

        var result = await Service().StartChatAsync(new(null, text, new AssistantClientContextDto("/workflows", null, null, null, null)), AssistantTestSupport.Seller, cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        addedConversations.Should().ContainSingle().Which.Title.Should().HaveLength(AssistantLimits.TitleLength);
        addedMessages.Should().ContainSingle().Which.Should().Match<AssistantMessage>(m => m.Role == "user" && m.Metadata.Contains("/workflows"));
        unitOfWork.Verify(u => u.SaveChangesAsync(cancellation.Token), Times.Once);
        result.Value.ConversationId.Should().Be(addedConversations[0].Id);
    }

    [TestMethod]
    public async Task StartChatAsync_WithForeignConversation_ReturnsNotFound()
    {
        var foreign = Conversation(Guid.NewGuid());
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { foreign }.AsQueryable().BuildMock());

        var result = await Service().StartChatAsync(new(foreign.Id, "hi", null), AssistantTestSupport.Seller);

        result.Error.Code.Should().Be("assistant.conversation_not_found");
    }

    [TestMethod]
    public async Task StartChatAsync_WithDeletedConversation_ReturnsNotFound()
    {
        var deleted = Conversation(deletedAt: Now.UtcDateTime);
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { deleted }.AsQueryable().BuildMock());

        var result = await Service().StartChatAsync(new(deleted.Id, "hi", null), AssistantTestSupport.Seller);

        result.Error.Code.Should().Be("assistant.conversation_not_found");
    }

    [TestMethod]
    public async Task StartChatAsync_LoadsAtMostHistoryWindowMessagesInChronologicalOrder()
    {
        var conversation = Conversation();
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { conversation }.AsQueryable().BuildMock());
        conversations.Setup(r => r.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        var history = Enumerable.Range(0, 25).Select(i => new AssistantMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = i % 2 == 0 ? "user" : "assistant",
            Content = $"m{i}",
            CreatedAt = Now.UtcDateTime.AddMinutes(i)
        }).ToList();
        messages.Setup(r => r.Query()).Returns(history.AsQueryable().BuildMock());

        var result = await Service().StartChatAsync(new(conversation.Id, "next", null), AssistantTestSupport.Seller);

        result.Value.History.Should().HaveCount(AssistantLimits.HistoryWindow);
        result.Value.History.First().Content.Should().Be("m5");
        result.Value.History.Last().Content.Should().Be("m24");
        conversation.UpdatedAt.Should().Be(Now.UtcDateTime);
    }

    [TestMethod]
    public async Task StartChatAsync_ReadsToolMemoryOfAssistantMessagesIntoHistory()
    {
        var conversation = Conversation();
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { conversation }.AsQueryable().BuildMock());
        conversations.Setup(r => r.GetByIdAsync(conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        var history = new List<AssistantMessage>
        {
            new() { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "user", Content = "list my workflows", Metadata = "{}", CreatedAt = Now.UtcDateTime },
            new()
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = "assistant",
                Content = "You have two workflows.",
                Metadata = """{"toolMemory":[{"tool":"list_workflows","result":"{\"items\":[{\"id\":\"w1\"}]}"}]}""",
                CreatedAt = Now.UtcDateTime.AddMinutes(1)
            }
        };
        messages.Setup(r => r.Query()).Returns(history.AsQueryable().BuildMock());

        var result = await Service().StartChatAsync(new(conversation.Id, "start the first one", null), AssistantTestSupport.Seller);

        result.Value.History[0].ToolMemory.Should().BeNull();
        result.Value.History[1].ToolMemory.Should().Contain("list_workflows").And.Contain("w1");
    }

    [TestMethod]
    public async Task GetConversationAsync_DoesNotExposeToolMemory()
    {
        var conversation = Conversation();
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { conversation }.AsQueryable().BuildMock());
        var message = new AssistantMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = "You have two workflows.",
            Metadata = """{"kind":"chat","toolMemory":[{"tool":"list_workflows","result":"{}"}]}""",
            CreatedAt = Now.UtcDateTime
        };
        messages.Setup(r => r.Query()).Returns(new List<AssistantMessage> { message }.AsQueryable().BuildMock());
        pendingActions.Setup(r => r.Query()).Returns(new List<AssistantPendingAction>().AsQueryable().BuildMock());

        var result = await Service().GetConversationAsync(conversation.Id);

        var metadata = result.Value.Messages.Single().Metadata;
        metadata.TryGetProperty("toolMemory", out _).Should().BeFalse();
        metadata.GetProperty("kind").GetString().Should().Be("chat");
    }

    [TestMethod]
    public async Task ListConversationsAsync_ReturnsOwnNonDeletedNewestFirst()
    {
        var older = Conversation();
        older.UpdatedAt = Now.UtcDateTime.AddHours(-2);
        var newer = Conversation();
        newer.UpdatedAt = Now.UtcDateTime.AddHours(-1);
        var foreign = Conversation(Guid.NewGuid());
        var deleted = Conversation(deletedAt: Now.UtcDateTime);
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { older, foreign, newer, deleted }.AsQueryable().BuildMock());

        var result = await Service().ListConversationsAsync();

        result.Value.Select(item => item.Id).Should().Equal(newer.Id, older.Id);
    }

    [TestMethod]
    public async Task GetConversationAsync_MarksElapsedPendingActionsAsExpired()
    {
        var conversation = Conversation();
        var action = Action(expiresAt: Now.UtcDateTime.AddMinutes(-1));
        action.ConversationId = conversation.Id;
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { conversation }.AsQueryable().BuildMock());
        pendingActions.Setup(r => r.Query()).Returns(new List<AssistantPendingAction> { action }.AsQueryable().BuildMock());

        var result = await Service().GetConversationAsync(conversation.Id);

        result.Value.PendingActions.Single().Status.Should().Be("expired");
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetConversationAsync_WhenForeign_ReturnsNotFound()
    {
        var foreign = Conversation(Guid.NewGuid());
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { foreign }.AsQueryable().BuildMock());

        var result = await Service().GetConversationAsync(foreign.Id);

        result.Error.Code.Should().Be("assistant.conversation_not_found");
    }

    [TestMethod]
    public async Task ConfirmActionAsync_WhenActionBelongsToAnotherUser_ReturnsNotFound()
    {
        ReturnAction(null);

        var result = await Service().ConfirmActionAsync(Guid.NewGuid());

        result.Error.Code.Should().Be("assistant.action_not_found");
    }

    [TestMethod]
    public async Task ConfirmActionAsync_WhenAlreadyExecuted_ReturnsConflict()
    {
        ReturnAction(Action("executed"));

        var result = await Service().ConfirmActionAsync(Guid.NewGuid());

        result.Error.Code.Should().Be("assistant.action_not_pending");
    }

    [TestMethod]
    public async Task ConfirmActionAsync_WhenExpired_MarksExpiredAndDoesNotRunTool()
    {
        var action = Action(expiresAt: Now.UtcDateTime.AddSeconds(-1));
        ReturnAction(action);

        var result = await Service().ConfirmActionAsync(action.Id);

        result.Error.Code.Should().Be("assistant.action_expired");
        action.Status.Should().Be("expired");
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        confirmTool.Verify(t => t.ExecuteConfirmedAsync(It.IsAny<Guid>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ConfirmActionAsync_WhenClaimLost_ReturnsConflictWithoutRunningTool()
    {
        var action = Action();
        ReturnAction(action);
        pendingActions.Setup(r => r.TryClaimForExecutionAsync(action.Id, AssistantTestSupport.UserId, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Service().ConfirmActionAsync(action.Id);

        result.IsFailure.Should().BeTrue();
        confirmTool.Verify(t => t.ExecuteConfirmedAsync(It.IsAny<Guid>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ConfirmActionAsync_WhenToolSucceeds_StoresResultAndAddsActionResultMessage()
    {
        using var cancellation = new CancellationTokenSource();
        var action = Action();
        ReturnAction(action);
        pendingActions.Setup(r => r.TryClaimForExecutionAsync(action.Id, AssistantTestSupport.UserId, Now.UtcDateTime, cancellation.Token)).ReturnsAsync(true);
        confirmTool.Setup(t => t.ExecuteConfirmedAsync(action.Id, It.IsAny<System.Text.Json.JsonElement>(), cancellation.Token))
            .ReturnsAsync(Result.Success(new AssistantConfirmedOutcome("Run started.", AssistantTestSupport.Json("""{"runId":"r1"}"""))));

        var result = await Service().ConfirmActionAsync(action.Id, cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Status.Should().Be("executed");
        action.Result.Should().Contain("r1");
        addedMessages.Single().Should().Match<AssistantMessage>(m => m.Content == "Run workflow: Run started." && m.Metadata.Contains("action_result"));
    }

    [TestMethod]
    public async Task ConfirmActionAsync_WhenToolFails_StoresFailureAndStillReturnsSuccess()
    {
        var action = Action();
        ReturnAction(action);
        pendingActions.Setup(r => r.TryClaimForExecutionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        confirmTool.Setup(t => t.ExecuteConfirmedAsync(It.IsAny<Guid>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AssistantConfirmedOutcome>(Error.Conflict("stale", "The workflow changed.")));

        var result = await Service().ConfirmActionAsync(action.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Match<APCS.Application.Features.Assistant.Dtos.Response.AssistantPendingActionResponseDto>(a =>
            a.Status == "failed" && a.ErrorMessage == "The workflow changed.");
    }

    [TestMethod]
    public async Task ConfirmActionAsync_WhenToolMissing_MarksFailed()
    {
        var action = Action();
        action.ToolName = "removed_tool";
        ReturnAction(action);
        pendingActions.Setup(r => r.TryClaimForExecutionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Service().ConfirmActionAsync(action.Id);

        result.Value.Action.Status.Should().Be("failed");
        result.Value.Action.ErrorMessage.Should().Be(AssistantErrors.ToolUnavailable().Message);
    }

    [TestMethod]
    public async Task RejectActionAsync_WhenPending_RejectsAndAddsCancelledMessage()
    {
        var action = Action();
        ReturnAction(action);
        pendingActions.Setup(r => r.TryRejectAsync(action.Id, AssistantTestSupport.UserId, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Service().RejectActionAsync(action.Id);

        result.Value.Action.Status.Should().Be("rejected");
        addedMessages.Single().Content.Should().Be("Cancelled: Run workflow.");
    }

    [TestMethod]
    public async Task RejectActionAsync_WhenNotPending_ReturnsConflict()
    {
        ReturnAction(Action("executed"));

        var result = await Service().RejectActionAsync(Guid.NewGuid());

        result.Error.Code.Should().Be("assistant.action_not_pending");
    }

    [TestMethod]
    public async Task DeleteConversationAsync_WhenOwned_SoftDeletesAndRequestsPurge()
    {
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        conversationStore.Setup(r => r.SoftDeleteAsync(AssistantTestSupport.UserId, id, null, Now.UtcDateTime, cancellation.Token)).ReturnsAsync(1);

        var result = await Service().DeleteConversationAsync(id, cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        purgeQueue.Verify(q => q.RequestPurge(), Times.Once);
    }

    [TestMethod]
    public async Task DeleteConversationAsync_WhenNotOwnedOrMissing_ReturnsNotFoundWithoutPurge()
    {
        conversationStore.Setup(r => r.SoftDeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await Service().DeleteConversationAsync(Guid.NewGuid());

        result.Error.Code.Should().Be("assistant.conversation_not_found");
        purgeQueue.Verify(q => q.RequestPurge(), Times.Never);
    }

    [TestMethod]
    public async Task DeleteConversationAsync_WhenUnauthenticated_ReturnsUnauthorized()
    {
        currentUser.SetupGet(u => u.UserId).Returns((Guid?)null);

        var result = await Service().DeleteConversationAsync(Guid.NewGuid());

        result.Error.Code.Should().Be("assistant.unauthorized");
        conversationStore.Verify(r => r.SoftDeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteConversationsAsync_KeepsTheGivenConversationAndRequestsPurge()
    {
        var keep = Guid.NewGuid();
        conversationStore.Setup(r => r.SoftDeleteAsync(AssistantTestSupport.UserId, null, keep, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var result = await Service().DeleteConversationsAsync(keep);

        result.IsSuccess.Should().BeTrue();
        purgeQueue.Verify(q => q.RequestPurge(), Times.Once);
    }

    [TestMethod]
    public async Task DeleteConversationsAsync_WhenNothingToDelete_SucceedsWithoutPurge()
    {
        conversationStore.Setup(r => r.SoftDeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await Service().DeleteConversationsAsync(null);

        result.IsSuccess.Should().BeTrue();
        purgeQueue.Verify(q => q.RequestPurge(), Times.Never);
    }

    private AssistantMessage MessageWithAction(Guid actionId, out AssistantConversation conversation, Guid? ownerId = null)
    {
        conversation = Conversation(ownerId);
        var message = new AssistantMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = "x",
            Metadata = $$$"""{"clientActions":[{"id":"{{{actionId}}}","kind":"edit_workflow_canvas","payload":{}}],"kind":"chat"}"""
        };
        messages.Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { conversation }.AsQueryable().BuildMock());
        return message;
    }

    [TestMethod]
    [DataRow("applied")]
    [DataRow("dismissed")]
    public async Task SetClientActionStateAsync_StoresTheStateInTheMessageMetadata(string state)
    {
        var actionId = Guid.NewGuid();
        var message = MessageWithAction(actionId, out _);

        var result = await Service().SetClientActionStateAsync(message.Id, actionId, new(state));

        result.IsSuccess.Should().BeTrue();
        message.Metadata.Should().Contain("clientActionStates").And.Contain(actionId.ToString()).And.Contain(state).And.Contain("\"kind\":\"chat\"");
        messages.Verify(r => r.UpdateAsync(message, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SetClientActionStateAsync_WithUnknownState_ReturnsValidationError()
    {
        var actionId = Guid.NewGuid();
        var message = MessageWithAction(actionId, out _);

        var result = await Service().SetClientActionStateAsync(message.Id, actionId, new("exploded"));

        result.Error.Type.Should().Be(ErrorType.Validation);
        messages.Verify(r => r.UpdateAsync(It.IsAny<AssistantMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SetClientActionStateAsync_ForAnotherUsersMessage_ReturnsNotFound()
    {
        var actionId = Guid.NewGuid();
        var message = MessageWithAction(actionId, out _, Guid.NewGuid());

        var result = await Service().SetClientActionStateAsync(message.Id, actionId, new("applied"));

        result.Error.Code.Should().Be("assistant.message_not_found");
    }

    [TestMethod]
    public async Task SetClientActionStateAsync_ForAnActionTheMessageDoesNotHave_ReturnsNotFound()
    {
        var message = MessageWithAction(Guid.NewGuid(), out _);

        var result = await Service().SetClientActionStateAsync(message.Id, Guid.NewGuid(), new("applied"));

        result.Error.Code.Should().Be("assistant.message_not_found");
    }

    [TestMethod]
    public async Task SetClientActionStateAsync_ForAMissingMessage_ReturnsNotFound()
    {
        messages.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync((AssistantMessage?)null);

        var result = await Service().SetClientActionStateAsync(Guid.NewGuid(), Guid.NewGuid(), new("applied"));

        result.Error.Code.Should().Be("assistant.message_not_found");
    }

    [TestMethod]
    public async Task GetConversationAsync_ReturnsPersistedClientActionStatesInMetadata()
    {
        var actionId = Guid.NewGuid();
        var conversation = Conversation();
        var message = new AssistantMessage { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = "x", Metadata = $$$"""{"clientActionStates":{"{{{actionId}}}":"applied"}}""", CreatedAt = Now.UtcDateTime };
        conversations.Setup(r => r.Query()).Returns(new List<AssistantConversation> { conversation }.AsQueryable().BuildMock());
        messages.Setup(r => r.Query()).Returns(new List<AssistantMessage> { message }.AsQueryable().BuildMock());

        var result = await Service().GetConversationAsync(conversation.Id);

        result.Value.Messages.Single().Metadata.GetRawText().Should().Contain("applied");
    }
}
