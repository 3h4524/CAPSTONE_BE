using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Assistant;

public sealed class AssistantService(
    ICurrentUser currentUser,
    IRepository<AssistantConversation> conversations,
    IRepository<AssistantMessage> messages,
    IAssistantPendingActionRepository pendingActions,
    IAssistantConversationRepository conversationStore,
    IAssistantConversationPurgeQueue purgeQueue,
    IUnitOfWork unitOfWork,
    IValidator<SendAssistantMessageRequestDto> sendValidator,
    AssistantAgentLoop agentLoop,
    AssistantToolRegistry registry,
    TimeProvider timeProvider) : IAssistantService
{
    private const string ToolMemoryKey = "toolMemory";

    public async Task<Result<AssistantChatSession>> StartChatAsync(
        SendAssistantMessageRequestDto request,
        AssistantCaller caller,
        CancellationToken cancellationToken = default)
    {
        var validation = await sendValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<AssistantChatSession>(validation.ToValidationError());
        if (currentUser.UserId is not { } userId)
            return Result.Failure<AssistantChatSession>(AssistantErrors.Unauthenticated());

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var text = request.Message.Trim();
        var history = new List<AssistantHistoryMessage>();
        Guid conversationId;
        if (request.ConversationId is { } existingId)
        {
            var exists = await conversations.Query()
                .AnyAsync(item => item.Id == existingId && item.UserId == userId && item.DeletedAt == null, cancellationToken);
            if (!exists)
                return Result.Failure<AssistantChatSession>(AssistantErrors.ConversationNotFound());

            conversationId = existingId;
            var recent = await messages.Query()
                .Where(item => item.ConversationId == conversationId)
                .OrderByDescending(item => item.CreatedAt)
                .Take(AssistantLimits.HistoryWindow)
                .Select(item => new { item.Role, item.Content, item.Metadata })
                .ToListAsync(cancellationToken);
            history.AddRange(recent.AsEnumerable().Reverse()
                .Where(item => !string.IsNullOrWhiteSpace(item.Content))
                .Select(item => new AssistantHistoryMessage(
                    item.Role,
                    item.Content,
                    item.Role == AssistantMessageRoles.Assistant ? ReadToolMemory(item.Metadata) : null)));
            var conversation = await conversations.GetByIdAsync(conversationId, cancellationToken);
            if (conversation is not null)
            {
                conversation.UpdatedAt = now;
                await conversations.UpdateAsync(conversation, cancellationToken: cancellationToken);
            }
        }
        else
        {
            conversationId = Guid.NewGuid();
            await conversations.AddAsync(new AssistantConversation
            {
                Id = conversationId,
                UserId = userId,
                Title = text.Length <= AssistantLimits.TitleLength ? text : text[..AssistantLimits.TitleLength],
                CreatedAt = now,
                UpdatedAt = now
            }, cancellationToken: cancellationToken);
        }

        var userMessageId = Guid.NewGuid();
        await messages.AddAsync(new AssistantMessage
        {
            Id = userMessageId,
            ConversationId = conversationId,
            Role = AssistantMessageRoles.User,
            Content = text,
            Metadata = new AssistantMessageMetadata(Route: request.Context?.Route).ToJson(),
            CreatedAt = now
        }, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AssistantChatSession(conversationId, userMessageId, userId, caller, text, request.Context, history));
    }

    public IAsyncEnumerable<AssistantStreamEvent> StreamAsync(
        AssistantChatSession session,
        CancellationToken cancellationToken = default) =>
        agentLoop.RunAsync(session, cancellationToken);

    public async Task<Result<IReadOnlyList<AssistantConversationSummaryResponseDto>>> ListConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<IReadOnlyList<AssistantConversationSummaryResponseDto>>(AssistantErrors.Unauthenticated());

        var items = await conversations.Query()
            .Where(item => item.UserId == userId && item.DeletedAt == null)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(AssistantLimits.ConversationListSize)
            .Select(item => new AssistantConversationSummaryResponseDto(item.Id, item.Title, item.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<AssistantConversationSummaryResponseDto>>(items);
    }

    public async Task<Result<AssistantConversationDetailResponseDto>> GetConversationAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<AssistantConversationDetailResponseDto>(AssistantErrors.Unauthenticated());

        var conversation = await conversations.Query()
            .Where(item => item.Id == id && item.UserId == userId && item.DeletedAt == null)
            .Select(item => new { item.Id, item.Title })
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null)
            return Result.Failure<AssistantConversationDetailResponseDto>(AssistantErrors.ConversationNotFound());

        var items = await messages.Query()
            .Where(item => item.ConversationId == id)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var actions = await pendingActions.Query()
            .Where(item => item.ConversationId == id && item.UserId == userId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return Result.Success(new AssistantConversationDetailResponseDto(
            conversation.Id,
            conversation.Title,
            items.Select(ToMessageDto).ToArray(),
            actions.Select(action => ToActionDto(action, now)).ToArray()));
    }

    public async Task<Result<AssistantActionResultResponseDto>> ConfirmActionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.Unauthenticated());

        var action = await FindOwnedActionAsync(id, userId, cancellationToken);
        if (action is null)
            return Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.ActionNotFound());
        if (action.Status != AssistantPendingActionStatuses.Pending)
            return Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.ActionNotPending(action.Status));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (action.ExpiresAt <= now)
        {
            action.Status = AssistantPendingActionStatuses.Expired;
            action.ResolvedAt = now;
            await pendingActions.UpdateAsync(action, cancellationToken: cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.ActionExpired());
        }

        if (!await pendingActions.TryClaimForExecutionAsync(id, userId, now, cancellationToken))
        {
            var current = await FindOwnedActionAsync(id, userId, cancellationToken);
            return Result.Failure<AssistantActionResultResponseDto>(
                AssistantErrors.ActionNotPending(current?.Status ?? AssistantPendingActionStatuses.Executing));
        }

        var tool = registry.FindConfirmable(action.ToolName);
        string outcomeMessage;
        if (tool is null)
        {
            action.Status = AssistantPendingActionStatuses.Failed;
            action.ErrorMessage = AssistantErrors.ToolUnavailable().Message;
            outcomeMessage = $"{action.Title} failed: {action.ErrorMessage}";
        }
        else
        {
            var arguments = JsonDocument.Parse(action.Arguments).RootElement.Clone();
            var result = await tool.ExecuteConfirmedAsync(action.Id, arguments, cancellationToken);
            if (result.IsSuccess)
            {
                action.Status = AssistantPendingActionStatuses.Executed;
                action.Result = result.Value.Payload.GetRawText();
                outcomeMessage = $"{action.Title}: {result.Value.Summary}";
            }
            else
            {
                action.Status = AssistantPendingActionStatuses.Failed;
                action.ErrorMessage = result.Error.Message;
                outcomeMessage = $"{action.Title} failed: {result.Error.Message}";
            }
        }

        action.ResolvedAt = now;
        return await ResolveAsync(action, outcomeMessage, now, cancellationToken);
    }

    public async Task<Result<AssistantActionResultResponseDto>> RejectActionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.Unauthenticated());

        var action = await FindOwnedActionAsync(id, userId, cancellationToken);
        if (action is null)
            return Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.ActionNotFound());
        if (action.Status != AssistantPendingActionStatuses.Pending)
            return Result.Failure<AssistantActionResultResponseDto>(AssistantErrors.ActionNotPending(action.Status));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!await pendingActions.TryRejectAsync(id, userId, now, cancellationToken))
        {
            var current = await FindOwnedActionAsync(id, userId, cancellationToken);
            return Result.Failure<AssistantActionResultResponseDto>(
                AssistantErrors.ActionNotPending(current?.Status ?? AssistantPendingActionStatuses.Executing));
        }

        action.Status = AssistantPendingActionStatuses.Rejected;
        action.ResolvedAt = now;
        return await ResolveAsync(action, $"Cancelled: {action.Title}.", now, cancellationToken, persistAction: false);
    }

    public async Task<Result> DeleteConversationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure(AssistantErrors.Unauthenticated());

        var deleted = await conversationStore.SoftDeleteAsync(userId, id, null, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (deleted == 0)
            return Result.Failure(AssistantErrors.ConversationNotFound());

        purgeQueue.RequestPurge();
        return Result.Success();
    }

    public async Task<Result> DeleteConversationsAsync(Guid? exceptConversationId, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure(AssistantErrors.Unauthenticated());

        var deleted = await conversationStore.SoftDeleteAsync(userId, null, exceptConversationId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (deleted > 0)
            purgeQueue.RequestPurge();
        return Result.Success();
    }

    public async Task<Result> SetClientActionStateAsync(
        Guid messageId,
        Guid actionId,
        SetClientActionStateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure(AssistantErrors.Unauthenticated());
        if (!AssistantClientActionStates.IsValid(request.State))
            return Result.Failure(AssistantErrors.InvalidActionState());

        var message = await messages.GetByIdAsync(messageId, cancellationToken);
        if (message is null)
            return Result.Failure(AssistantErrors.MessageNotFound());
        var ownsConversation = await conversations.Query()
            .AnyAsync(item => item.Id == message.ConversationId && item.UserId == userId && item.DeletedAt == null, cancellationToken);
        if (!ownsConversation)
            return Result.Failure(AssistantErrors.MessageNotFound());

        var metadata = JsonNode.Parse(message.Metadata) as JsonObject ?? new JsonObject();
        var knowsAction = metadata["clientActions"] is JsonArray actions
            && actions.OfType<JsonObject>().Any(action => string.Equals(action["id"]?.GetValue<string>(), actionId.ToString(), StringComparison.OrdinalIgnoreCase));
        if (!knowsAction)
            return Result.Failure(AssistantErrors.MessageNotFound());

        var states = metadata["clientActionStates"] as JsonObject ?? new JsonObject();
        states[actionId.ToString()] = request.State;
        metadata["clientActionStates"] = states;
        message.Metadata = metadata.ToJsonString();
        await messages.UpdateAsync(message, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<AssistantPendingAction?> FindOwnedActionAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        (await pendingActions.FindAsync(item => item.Id == id && item.UserId == userId, cancellationToken)).FirstOrDefault();

    private async Task<Result<AssistantActionResultResponseDto>> ResolveAsync(
        AssistantPendingAction action,
        string messageText,
        DateTime now,
        CancellationToken cancellationToken,
        bool persistAction = true)
    {
        var message = new AssistantMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = action.ConversationId,
            Role = AssistantMessageRoles.Assistant,
            Content = messageText,
            Metadata = new AssistantMessageMetadata(Kind: AssistantMessageKinds.ActionResult).ToJson(),
            CreatedAt = now
        };
        await messages.AddAsync(message, cancellationToken: cancellationToken);
        if (persistAction)
            await pendingActions.UpdateAsync(action, cancellationToken: cancellationToken);
        var conversation = await conversations.GetByIdAsync(action.ConversationId, cancellationToken);
        if (conversation is not null)
        {
            conversation.UpdatedAt = now;
            await conversations.UpdateAsync(conversation, cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new AssistantActionResultResponseDto(ToActionDto(action, now), ToMessageDto(message)));
    }

    private static AssistantMessageResponseDto ToMessageDto(AssistantMessage message) =>
        new(message.Id, message.Role, message.Content, PublicMetadata(message.Metadata), message.CreatedAt);

    private static JsonElement PublicMetadata(string metadataJson)
    {
        if (JsonNode.Parse(metadataJson) is not JsonObject metadata)
            return JsonDocument.Parse(metadataJson).RootElement.Clone();
        metadata.Remove(ToolMemoryKey);
        return JsonSerializer.SerializeToElement(metadata);
    }

    private static string? ReadToolMemory(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;
        try
        {
            return JsonNode.Parse(metadataJson) is JsonObject metadata && metadata[ToolMemoryKey] is JsonArray memory && memory.Count > 0
                ? memory.ToJsonString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AssistantPendingActionResponseDto ToActionDto(AssistantPendingAction action, DateTime now)
    {
        var status = action.Status == AssistantPendingActionStatuses.Pending && action.ExpiresAt <= now
            ? AssistantPendingActionStatuses.Expired
            : action.Status;
        JsonElement? result = string.IsNullOrWhiteSpace(action.Result) ? null : JsonDocument.Parse(action.Result).RootElement.Clone();
        return new AssistantPendingActionResponseDto(
            action.Id, action.ToolName, action.Title, action.Summary, status, action.ExpiresAt, result, action.ErrorMessage);
    }
}
