using System.Security.Claims;
using APCS.Api.Controllers;
using APCS.Api.Extensions;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AssistantControllerTests
{
    private readonly Mock<IAssistantService> service = new(MockBehavior.Strict);

    private AssistantController Controller(params string[] roles)
    {
        var identity = new ClaimsIdentity(roles.Select(role => new Claim(ClaimTypes.Role, role)), "test");
        return new AssistantController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } }
        };
    }

    private static async IAsyncEnumerable<AssistantStreamEvent> NoEvents()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static AssistantChatSession Session() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new AssistantCaller(true, false), "hi", null, []);

    [TestMethod]
    public async Task Chat_WhenStartFails_ReturnsProblemDetails()
    {
        var request = new SendAssistantMessageRequestDto(null, "", null);
        service.Setup(s => s.StartChatAsync(request, It.IsAny<AssistantCaller>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AssistantChatSession>(Error.Validation("Type a message.")));

        var result = await Controller(AuthConstants.UserRole).Chat(request, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task Chat_WhenStartSucceeds_ReturnsServerSentEventsResult()
    {
        using var cancellation = new CancellationTokenSource();
        var session = Session();
        var request = new SendAssistantMessageRequestDto(null, "hi", null);
        service.Setup(s => s.StartChatAsync(request, It.IsAny<AssistantCaller>(), cancellation.Token)).ReturnsAsync(Result.Success(session));
        service.Setup(s => s.StreamAsync(session, cancellation.Token)).Returns(NoEvents());

        var result = await Controller(AuthConstants.UserRole).Chat(request, cancellation.Token);

        result.Should().BeOfType<ServerSentEventsResult>();
        service.VerifyAll();
    }

    [TestMethod]
    [DataRow(AuthConstants.UserRole, true, false)]
    [DataRow(AuthConstants.AdminRole, false, true)]
    public async Task Chat_PassesRolesToCaller(string role, bool isSeller, bool isAdmin)
    {
        AssistantCaller? captured = null;
        var request = new SendAssistantMessageRequestDto(null, "hi", null);
        service.Setup(s => s.StartChatAsync(request, It.IsAny<AssistantCaller>(), It.IsAny<CancellationToken>()))
            .Callback<SendAssistantMessageRequestDto, AssistantCaller, CancellationToken>((_, caller, _) => captured = caller)
            .ReturnsAsync(Result.Failure<AssistantChatSession>(Error.Validation("x")));

        await Controller(role).Chat(request, default);

        captured.Should().Be(new AssistantCaller(isSeller, isAdmin));
    }

    [TestMethod]
    public async Task ConversationAndActionEndpoints_ForwardRequestsAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();
        IReadOnlyList<AssistantConversationSummaryResponseDto> list = [new(id, "t", DateTime.UtcNow)];
        var detail = new AssistantConversationDetailResponseDto(id, "t", [], []);
        var action = new AssistantActionResultResponseDto(
            new(id, "tool", "Title", "Summary", "executed", DateTime.UtcNow, null, null),
            new(Guid.NewGuid(), "assistant", "done", default, DateTime.UtcNow));
        service.Setup(s => s.ListConversationsAsync(cancellation.Token)).ReturnsAsync(Result.Success(list));
        service.Setup(s => s.GetConversationAsync(id, cancellation.Token)).ReturnsAsync(Result.Success(detail));
        service.Setup(s => s.ConfirmActionAsync(id, cancellation.Token)).ReturnsAsync(Result.Success(action));
        service.Setup(s => s.RejectActionAsync(id, cancellation.Token)).ReturnsAsync(Result.Success(action));
        var controller = Controller(AuthConstants.UserRole);

        (await controller.ListConversations(cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(list);
        (await controller.GetConversation(id, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(detail);
        (await controller.ConfirmAction(id, cancellation.Token)).Should().BeOfType<OkObjectResult>();
        (await controller.RejectAction(id, cancellation.Token)).Should().BeOfType<OkObjectResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public async Task ConfirmAction_WhenConflict_ReturnsConflictProblem()
    {
        service.Setup(s => s.ConfirmActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.ActionExpired()));

        var result = await Controller(AuthConstants.UserRole).ConfirmAction(Guid.NewGuid(), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [TestMethod]
    public void Controller_IsRestrictedToSellerAndAdminRoles()
    {
        var authorize = typeof(AssistantController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

        authorize.Roles.Should().Contain(AuthConstants.UserRole).And.Contain(AuthConstants.AdminRole);
    }

    [TestMethod]
    public async Task DeleteConversation_ReturnsAcceptedAndForwardsIdAndToken()
    {
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();
        service.Setup(s => s.DeleteConversationAsync(id, cancellation.Token)).ReturnsAsync(Result.Success());

        var result = await Controller(AuthConstants.UserRole).DeleteConversation(id, cancellation.Token);

        result.Should().BeOfType<AcceptedResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public async Task DeleteConversation_WhenNotFound_ReturnsProblem404()
    {
        service.Setup(s => s.DeleteConversationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(AssistantErrors.ConversationNotFound()));

        var result = await Controller(AuthConstants.UserRole).DeleteConversation(Guid.NewGuid(), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task DeleteConversations_PassesTheConversationToKeep()
    {
        var keep = Guid.NewGuid();
        service.Setup(s => s.DeleteConversationsAsync(keep, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var result = await Controller(AuthConstants.UserRole).DeleteConversations(keep, default);

        result.Should().BeOfType<AcceptedResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public async Task SetClientActionState_ReturnsNoContent()
    {
        var messageId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var request = new SetClientActionStateRequestDto("applied");
        service.Setup(s => s.SetClientActionStateAsync(messageId, actionId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var result = await Controller(AuthConstants.UserRole).SetClientActionState(messageId, actionId, request, default);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task SetClientActionState_WhenInvalid_ReturnsProblem400()
    {
        service.Setup(s => s.SetClientActionStateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SetClientActionStateRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(AssistantErrors.InvalidActionState()));

        var result = await Controller(AuthConstants.UserRole).SetClientActionState(Guid.NewGuid(), Guid.NewGuid(), new("x"), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
