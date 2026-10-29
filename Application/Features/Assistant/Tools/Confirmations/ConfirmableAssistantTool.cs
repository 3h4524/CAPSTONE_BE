using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record AssistantPendingActionDraft(string Title, string Summary, object Arguments);

public abstract class ConfirmableAssistantTool<TArgs>(
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : AssistantTool<TArgs>, IAssistantConfirmableTool where TArgs : class
{
    public sealed override AssistantToolKind Kind => AssistantToolKind.Confirmation;

    protected virtual string? ReplyOf(TArgs arguments) => null;

    protected abstract Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        TArgs arguments,
        CancellationToken cancellationToken);

    public abstract Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken);

    protected sealed override async Task<AssistantToolOutcome> ExecuteCoreAsync(
        AssistantToolContext context,
        TArgs arguments,
        CancellationToken cancellationToken)
    {
        var draft = await PreflightAsync(context, arguments, cancellationToken);
        if (draft.IsFailure)
            return AssistantToolOutcome.Error(draft.Error.Code.ToLowerInvariant(), draft.Error.Message);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var action = new AssistantPendingAction
        {
            Id = Guid.NewGuid(),
            ConversationId = context.ConversationId,
            UserId = context.UserId,
            ToolName = Name,
            Arguments = AssistantJson.Serialize(draft.Value.Arguments),
            Title = draft.Value.Title,
            Summary = draft.Value.Summary,
            Status = AssistantPendingActionStatuses.Pending,
            ExpiresAt = now.AddMinutes(AssistantLimits.PendingActionTtlMinutes),
            CreatedAt = now
        };
        await pendingActions.AddAsync(action, saveChange: true, cancellationToken);

        var data = new AssistantPendingActionEventData(action.Id, Name, action.Title, action.Summary, action.Status, action.ExpiresAt);
        return AssistantToolOutcome.Ok(
            new { status = "awaiting_confirmation", title = action.Title },
            true,
            AssistantStreamEvent.PendingAction(data)).WithFinalText(ReplyOf(arguments));
    }

    protected static string ValidationCode(Error error) => string.IsNullOrWhiteSpace(error.Code) ? "failed" : error.Code;
}
