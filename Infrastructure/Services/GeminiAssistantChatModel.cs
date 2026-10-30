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

public sealed class GeminiAssistantChatModel : IAssistantChatModel
{
    private readonly AssistantOptions settings;
    private readonly ILogger<GeminiAssistantChatModel> logger;
    private readonly Lazy<IChatClient>[] chatClients;
    private int currentKeyIndex;

    public GeminiAssistantChatModel(IOptions<AssistantOptions> options, ILogger<GeminiAssistantChatModel> logger)
        : this(options, logger, apiKey => new Client(apiKey: apiKey).AsIChatClient(options.Value.Model))
    {
    }

    internal GeminiAssistantChatModel(
        IOptions<AssistantOptions> options,
        ILogger<GeminiAssistantChatModel> logger,
        Func<string, IChatClient> clientFactory)
    {
        settings = options.Value;
        this.logger = logger;
        chatClients = settings.EffectiveApiKeys
            .Select(apiKey => new Lazy<IChatClient>(() => clientFactory(apiKey)))
            .ToArray();
    }

    public string ModelName => settings.Model;

    public async IAsyncEnumerable<AssistantModelUpdate> StreamAsync(
        AssistantModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (chatClients.Length == 0)
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
        var keyIndex = Volatile.Read(ref currentKeyIndex) % chatClients.Length;
        var keysTried = 1;
        var delayedRetries = 0;
        for (var attempt = 0; ; attempt++)
        {
            Exception? failure = null;
            await using (var enumerator = chatClients[keyIndex].Value
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
            if (updates.Count == 0 && IsRateLimited(failure) && keysTried < chatClients.Length)
            {
                var nextKeyIndex = (keyIndex + 1) % chatClients.Length;
                logger.LogWarning("Assistant API key {KeyIndex} is rate limited, switching to key {NextKeyIndex}", keyIndex, nextKeyIndex);
                keyIndex = nextKeyIndex;
                keysTried++;
                Volatile.Write(ref currentKeyIndex, keyIndex);
                continue;
            }

            if (updates.Count == 0 && delayedRetries < settings.RateLimitRetries && (IsRateLimited(failure) || IsOverloaded(failure)))
            {
                delayedRetries++;
                var delaySeconds = IsRateLimited(failure) ? settings.RateLimitRetryDelaySeconds : settings.OverloadRetryDelaySeconds;
                yield return AssistantModelUpdate.Retrying(delaySeconds);
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
