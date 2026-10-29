using System.Runtime.CompilerServices;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class GeminiAssistantChatModel(
    IOptions<AssistantOptions> options,
    ILogger<GeminiAssistantChatModel> logger) : IAssistantChatModel
{
    private readonly AssistantOptions settings = options.Value;
    private readonly Lazy<IChatClient> chatClient = new(() =>
        new Client(apiKey: options.Value.ApiKey).AsIChatClient(options.Value.Model));

    public string ModelName => settings.Model;

    public async IAsyncEnumerable<AssistantModelUpdate> StreamAsync(
        AssistantModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.IsConfigured)
        {
            yield return AssistantModelUpdate.Failed("not_configured");
            yield break;
        }

        var messages = request.Messages.SelectMany(ToChatMessages).ToList();
        var allowTools = request.AllowTools && request.Tools.Count > 0;
        var chatOptions = new ChatOptions
        {
            Temperature = settings.Temperature,
            MaxOutputTokens = settings.MaxOutputTokens,
            Tools = allowTools ? request.Tools.Select(ToDeclaration).ToList() : null,
            ToolMode = allowTools ? ChatToolMode.Auto : ChatToolMode.None
        };

        var updates = new List<ChatResponseUpdate>();
        for (var attempt = 0; ; attempt++)
        {
            Exception? failure = null;
            await using (var enumerator = chatClient.Value
                .GetStreamingResponseAsync(messages, chatOptions, cancellationToken)
                .GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        break;
                    }

                    if (!hasNext)
                        break;

                    var update = enumerator.Current;
                    updates.Add(update);
                    if (!string.IsNullOrEmpty(update.Text))
                        yield return AssistantModelUpdate.TextDelta(update.Text);
                }
            }

            if (failure is null)
                break;

            logger.LogWarning("Assistant model stream failed with {ExceptionType} on attempt {Attempt}", failure.GetType().Name, attempt + 1);
            if (updates.Count == 0 && attempt < settings.RateLimitRetries && IsRateLimited(failure))
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.RateLimitRetryDelaySeconds), cancellationToken);
                continue;
            }

            yield return AssistantModelUpdate.Failed("model_unavailable");
            yield break;
        }

        var response = updates.ToChatResponse();
        var toolCalls = response.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(call => new AssistantToolCall(
                call.CallId,
                call.Name,
                JsonSerializer.SerializeToElement(call.Arguments ?? new Dictionary<string, object?>())))
            .ToArray();
        var completed = new AssistantModelMessage(
            AssistantMessageRole.Assistant,
            response.Text,
            toolCalls,
            ProviderState: response.Messages.ToList());
        yield return AssistantModelUpdate.Completed(
            completed,
            (int?)response.Usage?.InputTokenCount,
            (int?)response.Usage?.OutputTokenCount,
            response.ModelId ?? settings.Model);
    }

    internal static bool IsRateLimited(Exception exception) =>
        exception.Message.Contains("quota", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("429", StringComparison.Ordinal)
        || exception.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);

    private static AITool ToDeclaration(AssistantToolDeclaration tool) =>
        AIFunctionFactory.CreateDeclaration(tool.Name, tool.Description, tool.ParametersSchema);

    private static IEnumerable<ChatMessage> ToChatMessages(AssistantModelMessage message) => message switch
    {
        { ProviderState: IEnumerable<ChatMessage> original } => original,
        { Role: AssistantMessageRole.System } => [new ChatMessage(ChatRole.System, message.Text)],
        { Role: AssistantMessageRole.User } => [new ChatMessage(ChatRole.User, message.Text)],
        { Role: AssistantMessageRole.Assistant } => [new ChatMessage(ChatRole.Assistant, message.Text)],
        { Role: AssistantMessageRole.Tool, ToolResult: { } result } =>
            [new ChatMessage(ChatRole.Tool, [new FunctionResultContent(result.CallId, JsonDocument.Parse(result.Json).RootElement.Clone())])],
        _ => []
    };
}
