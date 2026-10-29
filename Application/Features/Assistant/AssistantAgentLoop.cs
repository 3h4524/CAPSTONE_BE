using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Tools;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Assistant;

public sealed class AssistantAgentLoop(
    IAssistantChatModel chatModel,
    AssistantPromptBuilder promptBuilder,
    AssistantToolRegistry registry,
    IRepository<AssistantConversation> conversations,
    IRepository<AssistantMessage> messages,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<AssistantAgentLoop> logger)
{
    public async IAsyncEnumerable<AssistantStreamEvent> RunAsync(
        AssistantChatSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AssistantStreamEvent.Conversation(session.ConversationId, session.UserMessageId);

        var toolContext = new AssistantToolContext(session.ConversationId, session.UserId, session.Caller, session.Context);
        var tools = registry.Resolve(toolContext);
        var declarations = tools.Select(tool => tool.Declaration).ToArray();
        var conversation = promptBuilder.Build(session).ToList();
        var state = new AssistantTurnState();
        var endTurn = false;

        try
        {
            for (var iteration = 1; iteration <= AssistantLimits.MaxToolIterations + 1; iteration++)
            {
                AssistantModelUpdate? completed = null;
                await foreach (var update in chatModel.StreamAsync(new(conversation, declarations, !endTurn), cancellationToken))
                {
                    if (update.Kind == AssistantModelUpdateKind.Text && update.Text is { } text)
                    {
                        state.AppendText(text);
                        yield return AssistantStreamEvent.Delta(text);
                    }
                    else if (update.Kind == AssistantModelUpdateKind.Failed)
                    {
                        var (code, message) = AssistantErrors.FromModelFailure(update.FailureCode);
                        state.Error = new AssistantErrorData(code, message);
                        yield return AssistantStreamEvent.Error(code, message);
                        yield break;
                    }
                    else if (update.Kind == AssistantModelUpdateKind.Completed)
                    {
                        completed = update;
                    }
                }

                state.RecordUsage(completed);
                if (endTurn || completed?.Message is not { ToolCalls.Count: > 0 } assistantTurn)
                    break;

                conversation.Add(assistantTurn);
                foreach (var call in assistantTurn.ToolCalls)
                {
                    yield return AssistantStreamEvent.ToolStarted(call.CallId, call.Name);
                    var outcome = await ExecuteToolSafelyAsync(toolContext, tools, call, cancellationToken);
                    foreach (var toolEvent in outcome.Events)
                    {
                        state.Record(toolEvent);
                        yield return toolEvent;
                    }

                    conversation.Add(AssistantModelMessage.Tool(new AssistantToolResult(call.CallId, call.Name, outcome.ModelResultJson)));
                    state.ToolCalls.Add(new AssistantToolCallData(call.Name, outcome.Succeeded));
                    yield return AssistantStreamEvent.ToolFinished(call.CallId, call.Name, outcome.Succeeded);
                    endTurn |= outcome.EndsTurn;
                }

                if (iteration >= AssistantLimits.MaxToolIterations)
                    endTurn = true;
            }
        }
        finally
        {
            state.AssistantMessageId = await SaveAssistantMessageAsync(session, state);
        }

        yield return AssistantStreamEvent.Done(state.AssistantMessageId, state.Model ?? chatModel.ModelName, state.InputTokens, state.OutputTokens);
    }

    private async Task<AssistantToolOutcome> ExecuteToolSafelyAsync(
        AssistantToolContext context,
        IReadOnlyList<IAssistantTool> tools,
        AssistantToolCall call,
        CancellationToken cancellationToken)
    {
        var tool = tools.FirstOrDefault(item => item.Name == call.Name);
        if (tool is null)
            return AssistantToolOutcome.Error("unknown_tool", "That tool does not exist.");
        try
        {
            return await tool.ExecuteAsync(context, call.Arguments, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Assistant tool {ToolName} failed with {ExceptionType}", call.Name, exception.GetType().Name);
            return AssistantToolOutcome.Error("tool_failed", "The tool failed. Tell the user to try again.");
        }
    }

    private async Task<Guid> SaveAssistantMessageAsync(AssistantChatSession session, AssistantTurnState state)
    {
        if (!state.HasContent)
            return Guid.Empty;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var message = new AssistantMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = session.ConversationId,
            Role = AssistantMessageRoles.Assistant,
            Content = state.Text,
            Metadata = state.BuildMetadata().ToJson(),
            Model = state.Model ?? chatModel.ModelName,
            InputTokens = state.InputTokens,
            OutputTokens = state.OutputTokens,
            CreatedAt = now
        };
        await messages.AddAsync(message, cancellationToken: CancellationToken.None);
        var conversation = await conversations.GetByIdAsync(session.ConversationId, CancellationToken.None);
        if (conversation is not null)
        {
            conversation.UpdatedAt = now;
            await conversations.UpdateAsync(conversation, cancellationToken: CancellationToken.None);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return message.Id;
    }

    private sealed class AssistantTurnState
    {
        private readonly StringBuilder text = new();
        private readonly List<AssistantCitationData> citations = [];
        private readonly List<AssistantClientActionData> clientActions = [];
        private readonly List<Guid> pendingActionIds = [];
        private AssistantChoicesData? choices;

        public List<AssistantToolCallData> ToolCalls { get; } = [];

        public AssistantErrorData? Error { get; set; }

        public string? Model { get; private set; }

        public int? InputTokens { get; private set; }

        public int? OutputTokens { get; private set; }

        public Guid AssistantMessageId { get; set; }

        public string Text => text.ToString();

        public bool HasContent => text.Length > 0 || Error is not null || ToolCalls.Count > 0;

        public void AppendText(string value) => text.Append(value);

        public void RecordUsage(AssistantModelUpdate? completed)
        {
            if (completed is null)
                return;
            Model = completed.Model ?? Model;
            InputTokens = (InputTokens ?? 0) + (completed.InputTokens ?? 0);
            OutputTokens = (OutputTokens ?? 0) + (completed.OutputTokens ?? 0);
        }

        public void Record(AssistantStreamEvent streamEvent)
        {
            switch (streamEvent.Data)
            {
                case AssistantCitationsEventData data:
                    citations.AddRange(data.Items.Where(item => citations.All(existing => existing.DocumentId != item.DocumentId)));
                    break;
                case AssistantClientActionData data:
                    clientActions.Add(data);
                    break;
                case AssistantPendingActionEventData data:
                    pendingActionIds.Add(data.Id);
                    break;
                case AssistantChoicesData data:
                    choices = data;
                    break;
            }
        }

        public AssistantMessageMetadata BuildMetadata() => new(
            citations.Count > 0 ? citations : null,
            ToolCalls.Count > 0 ? ToolCalls : null,
            clientActions.Count > 0 ? clientActions : null,
            pendingActionIds.Count > 0 ? pendingActionIds : null,
            choices,
            Error,
            AssistantMessageKinds.Chat);
    }
}
