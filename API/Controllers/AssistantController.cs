using APCS.Api.Extensions;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = $"{AuthConstants.UserRole},{AuthConstants.AdminRole}")]
[Route("api/assistant")]
public sealed class AssistantController(IAssistantService assistant) : ControllerBase
{
    [HttpPost("chat")]
    [EnableRateLimiting(AssistantRateLimiting.ChatPolicy)]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Chat(SendAssistantMessageRequestDto request, CancellationToken cancellationToken)
    {
        var caller = new AssistantCaller(User.IsInRole(AuthConstants.UserRole), User.IsInRole(AuthConstants.AdminRole));
        var session = await assistant.StartChatAsync(request, caller, cancellationToken);
        return session.IsSuccess
            ? new ServerSentEventsResult(assistant.StreamAsync(session.Value, cancellationToken))
            : session.ToActionResult(this);
    }

    [HttpGet("conversations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(IReadOnlyList<AssistantConversationSummaryResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListConversations(CancellationToken cancellationToken) =>
        (await assistant.ListConversationsAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("conversations/{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AssistantConversationDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConversation(Guid id, CancellationToken cancellationToken) =>
        (await assistant.GetConversationAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("actions/{id:guid}/confirm")]
    [ProducesResponseType(typeof(AssistantActionResultResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmAction(Guid id, CancellationToken cancellationToken) =>
        (await assistant.ConfirmActionAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("actions/{id:guid}/reject")]
    [ProducesResponseType(typeof(AssistantActionResultResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectAction(Guid id, CancellationToken cancellationToken) =>
        (await assistant.RejectActionAsync(id, cancellationToken)).ToActionResult(this);
}
