using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Rag.Dtos;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Assistant;

public sealed partial class AssistantAgentLoop(
    IAssistantChatModel chatModel,
    AssistantPromptBuilder promptBuilder,
    AssistantToolRegistry registry,
    AssistantKnowledgeBase knowledgeBase,
    IRepository<AssistantConversation> conversations,
    IRepository<AssistantMessage> messages,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<AssistantAgentLoop> logger)
{
    private const string KnowledgeToolName = "search_faq";
    private const string PrefetchCallId = "prefetch";
    private const string CachedModelName = "cache";
    private const int ReplayChunkChars = 48;
    private const int MaxEmptyRetries = 1;
    private const string EmptyResponseMessage = "The assistant could not come up with an answer. Please rephrase or try again.";
    private const string CompletionDoneMarker = "DONE";
    private const string CompletionCheckNote =
        "<completion_check>Read the user's latest message again. If a question in it is not answered yet, answer it now in plain text. "
        + "If a requested change or action has not been made yet, call the tool for it now. "
        + "Never summarise or repeat what you already did. If everything is done, reply with exactly DONE and nothing else.</completion_check>";

    public async IAsyncEnumerable<AssistantStreamEvent> RunAsync(
        AssistantChatSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return AssistantStreamEvent.Conversation(session.ConversationId, session.UserMessageId);

        var toolContext = new AssistantToolContext(session.ConversationId, session.UserId, session.Caller, session.Context);
        var state = new AssistantTurnState();

        var cachedAnswer = await knowledgeBase.GetAnswerAsync(session, cancellationToken);
        if (cachedAnswer is not null)
        {
            foreach (var cachedEvent in ReplayCachedAnswer(cachedAnswer, state))
                yield return cachedEvent;
            state.AssistantMessageId = await SaveAssistantMessageAsync(session, state);
            yield return AssistantStreamEvent.Done(state.AssistantMessageId, CachedModelName, 0, 0);
            yield break;
        }

        IReadOnlyList<RagSearchResultDto> articles = [];
        if (AssistantKnowledgeBase.ShouldPrefetch(session))
        {
            yield return AssistantStreamEvent.ToolStarted(PrefetchCallId, KnowledgeToolName);
            articles = await knowledgeBase.PrefetchAsync(session, cancellationToken);
            state.ToolCalls.Add(new AssistantToolCallData(KnowledgeToolName, true));
            yield return AssistantStreamEvent.ToolFinished(PrefetchCallId, KnowledgeToolName, true);
        }

        var tools = registry.Resolve(toolContext);
        var declarations = tools.Select(tool => tool.Declaration).ToArray();
        var conversation = promptBuilder.Build(session, articles).ToList();
        var endTurn = false;
        var completionCheckUsed = false;
        var isCompletionCheck = false;
        var emptyRetriesLeft = MaxEmptyRetries;

        try
        {
            for (var iteration = 1; iteration <= AssistantLimits.MaxToolIterations + 1; iteration++)
            {
                AssistantModelUpdate? completed = null;
                var textBefore = state.Text.Length;
                var heldText = new StringBuilder();
                var completionPass = isCompletionCheck;
                isCompletionCheck = false;
                await foreach (var update in chatModel.StreamAsync(new(conversation, declarations, !endTurn), cancellationToken))
                {
                    if (update.Kind == AssistantModelUpdateKind.Text && update.Text is { } text)
                    {
                        if (completionPass)
                        {
                            heldText.Append(text);
                            continue;
                        }

                        var delta = state.Text.Length == textBefore ? state.WithParagraphBreak(text) : text;
                        state.AppendText(delta);
                        yield return AssistantStreamEvent.Delta(delta);
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
                    else if (update.Kind == AssistantModelUpdateKind.Retrying)
                    {
                        yield return AssistantStreamEvent.Status(update.RetryInSeconds ?? 0);
                    }
                }

                var completionText = heldText.ToString().Trim();
                if (completionPass && completionText.Length > 0 && !IsCompletionDone(completionText) && !RepeatsEarlierText(state.Text, completionText))
                {
                    var delta = state.WithParagraphBreak(completionText);
                    state.AppendText(delta);
                    yield return AssistantStreamEvent.Delta(delta);
                }

                state.RecordUsage(completed);
                if (!completionPass && completed?.Message is not { ToolCalls.Count: > 0 } && state.Text.Length == textBefore && !state.HasVisibleOutput && emptyRetriesLeft > 0)
                {
                    emptyRetriesLeft--;
                    iteration--;
                    continue;
                }

                if (endTurn || completed?.Message is not { ToolCalls.Count: > 0 } assistantTurn)
                    break;

                conversation.Add(assistantTurn);
                var modelWroteText = state.Text.Length > textBefore;
                var roundEndsTurn = false;
                var roundAsksUser = false;
                var roundHasReply = modelWroteText;
                var moreRequests = false;
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
                    if (outcome.Succeeded && call.Name != KnowledgeToolName && tools.FirstOrDefault(tool => tool.Name == call.Name)?.Kind == AssistantToolKind.Query)
                        state.Remember(call.Name, outcome.ModelResultJson);
                    yield return AssistantStreamEvent.ToolFinished(call.CallId, call.Name, outcome.Succeeded);
                    if (!outcome.EndsTurn)
                        continue;

                    roundEndsTurn = true;
                    roundAsksUser |= tools.FirstOrDefault(tool => tool.Name == call.Name)?.Kind == AssistantToolKind.Interaction;
                    moreRequests |= AssistantToolSchemas.RequestsMore(call.Arguments);
                    roundHasReply |= outcome.FinalText is not null;
                    if (outcome.FinalText is { Length: > 0 } finalText && !modelWroteText)
                    {
                        var reply = state.WithParagraphBreak(finalText);
                        state.AppendText(reply);
                        yield return AssistantStreamEvent.Delta(reply);
                    }
                }

                completionCheckUsed |= moreRequests;
                if (roundEndsTurn && !moreRequests)
                {
                    if (!roundHasReply)
                    {
                        endTurn = true;
                    }
                    else if (completionCheckUsed || roundAsksUser || iteration >= AssistantLimits.MaxToolIterations)
                    {
                        break;
                    }
                    else
                    {
                        completionCheckUsed = true;
                        isCompletionCheck = true;
                        conversation.Add(AssistantModelMessage.User(CompletionCheckNote));
                    }
                }

                if (iteration >= AssistantLimits.MaxToolIterations)
                    endTurn = true;
            }

            if (!state.HasVisibleOutput)
            {
                state.Error = new AssistantErrorData(AssistantErrors.EmptyResponseCode, EmptyResponseMessage);
                yield return AssistantStreamEvent.Error(AssistantErrors.EmptyResponseCode, EmptyResponseMessage);
                yield break;
            }

            if (state.UsedOnlyKnowledgeTools && articles.Count > 0)
            {
                var citations = articles
                    .GroupBy(article => article.DocumentId)
                    .Select(group => new AssistantCitationData(group.Key, group.First().Title, group.First().SourceUrl))
                    .ToArray();
                var citationsEvent = AssistantStreamEvent.Citations(citations);
                state.Record(citationsEvent);
                yield return citationsEvent;
            }
        }
        finally
        {
            state.AssistantMessageId = await SaveAssistantMessageAsync(session, state);
        }

        if (state.UsedOnlyKnowledgeTools && state.Error is null)
            await knowledgeBase.SaveAnswerAsync(session, new AssistantCachedAnswer(state.Text, state.Citations), CancellationToken.None);

        yield return AssistantStreamEvent.Done(state.AssistantMessageId, state.Model ?? chatModel.ModelName, state.InputTokens, state.OutputTokens);
    }

    private const double RepeatedWordShare = 0.6;

    private static bool IsCompletionDone(string text) =>
        string.Equals(text.Trim().TrimEnd('.', '!'), CompletionDoneMarker, StringComparison.OrdinalIgnoreCase);

    private static bool RepeatsEarlierText(string earlierText, string text)
    {
        var earlierWords = Words(earlierText).ToHashSet(StringComparer.Ordinal);
        var words = Words(text).ToArray();
        return words.Length > 0 && words.Count(earlierWords.Contains) >= RepeatedWordShare * words.Length;
    }

    private static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant()).Select(match => match.Value).Where(word => word.Length > 1);

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    private static IEnumerable<AssistantStreamEvent> ReplayCachedAnswer(AssistantCachedAnswer answer, AssistantTurnState state)
    {
        yield return AssistantStreamEvent.ToolStarted(PrefetchCallId, KnowledgeToolName);
        state.ToolCalls.Add(new AssistantToolCallData(KnowledgeToolName, true));
        yield return AssistantStreamEvent.ToolFinished(PrefetchCallId, KnowledgeToolName, true);
        if (answer.Citations.Count > 0)
        {
            var citationsEvent = AssistantStreamEvent.Citations(answer.Citations);
            state.Record(citationsEvent);
            yield return citationsEvent;
        }

        for (var offset = 0; offset < answer.Text.Length; offset += ReplayChunkChars)
        {
            var chunk = answer.Text.Substring(offset, Math.Min(ReplayChunkChars, answer.Text.Length - offset));
            state.AppendText(chunk);
            yield return AssistantStreamEvent.Delta(chunk);
        }
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
        private readonly List<AssistantToolMemoryData> toolMemory = [];
        private AssistantChoicesData? choices;

        public List<AssistantToolCallData> ToolCalls { get; } = [];

        public AssistantErrorData? Error { get; set; }

        public string? Model { get; private set; }

        public int? InputTokens { get; private set; }

        public int? OutputTokens { get; private set; }

        public Guid AssistantMessageId { get; set; }

        public string Text => text.ToString();

        public IReadOnlyList<AssistantCitationData> Citations => citations;

        public bool HasVisibleOutput => text.Length > 0 || clientActions.Count > 0 || pendingActionIds.Count > 0 || choices is not null;

        public bool UsedOnlyKnowledgeTools => ToolCalls.All(call => call.Name == KnowledgeToolName) && clientActions.Count == 0 && pendingActionIds.Count == 0 && choices is null;

        public bool HasContent => text.Length > 0 || Error is not null || ToolCalls.Count > 0;

        public void AppendText(string value) => text.Append(value);

        public string WithParagraphBreak(string value) =>
            text.Length == 0 || text[^1] == '\n' || value.StartsWith('\n') ? value : $"\n\n{value}";

        public void Remember(string toolName, string resultJson)
        {
            var result = AssistantJson.Truncate(resultJson, AssistantLimits.ToolMemoryResultChars);
            if (toolMemory.Sum(item => item.Result.Length) + result.Length <= AssistantLimits.ToolMemoryMessageChars)
                toolMemory.Add(new AssistantToolMemoryData(toolName, result));
        }

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
            AssistantMessageKinds.Chat,
            ToolMemory: toolMemory.Count > 0 ? toolMemory : null);
    }
}
