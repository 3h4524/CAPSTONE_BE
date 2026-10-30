using System.Runtime.CompilerServices;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using Google.GenAI;
using Google.GenAI.Types;
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
            Tools = request.Tools.Count > 0 ? request.Tools.Select(ToDeclaration).ToList() : null,
            ToolMode = allowTools ? ChatToolMode.Auto : ChatToolMode.None,
            RawRepresentationFactory = _ => new GenerateContentConfig { ThinkingConfig = new ThinkingConfig { ThinkingLevel = ResolveThinkingLevel() } }
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
            if (updates.Count == 0 && attempt < settings.RateLimitRetries && (IsRateLimited(failure) || IsOverloaded(failure)))
            {
                var delaySeconds = IsRateLimited(failure) ? settings.RateLimitRetryDelaySeconds : settings.OverloadRetryDelaySeconds;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
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

    private ThinkingLevel ResolveThinkingLevel() => settings.ThinkingLevel.Trim().ToLowerInvariant() switch
    {
        "minimal" => ThinkingLevel.Minimal,
        "medium" => ThinkingLevel.Medium,
        "high" => ThinkingLevel.High,
        _ => ThinkingLevel.Low
    };

    internal static bool IsOverloaded(Exception exception) =>
        StatusCodeOf(exception) is StatusCodes.InternalError or StatusCodes.Unavailable or StatusCodes.DeadlineExceeded;

    internal static bool IsRateLimited(Exception exception) =>
        StatusCodeOf(exception) == StatusCodes.TooManyRequests;

    private static int? StatusCodeOf(Exception exception) => exception switch
    {
        ApiException apiException => apiException.StatusCode,
        HttpRequestException { StatusCode: { } statusCode } => (int)statusCode,
        _ => null
    };

    private static class StatusCodes
    {
        public const int TooManyRequests = 429;
        public const int InternalError = 500;
        public const int Unavailable = 503;
        public const int DeadlineExceeded = 504;
    }

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
