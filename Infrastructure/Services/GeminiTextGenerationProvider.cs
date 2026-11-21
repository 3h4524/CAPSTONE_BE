using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class GeminiTextGenerationProvider(
    HttpClient httpClient,
    IOptions<GeminiTextGenerationOptions> options) : IGeminiTextGenerationProvider
{
    private static readonly TimeSpan OverloadRetryDelay = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GeminiTextGenerationOptions settings = options.Value;

    public bool IsConfigured => settings.IsConfigured;

    public async Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var parts = BuildParts(request);
        var models = Models(request);
        GenerateContentResponse? payload = null;
        var model = models[0];
        var attemptTimeoutSeconds = AttemptTimeout(request);
        for (var attempt = 0; ; attempt++)
        {
            model = models[attempt];
            var last = attempt == models.Length - 1;
            using var httpRequest = CreateRequest(request, parts, $"models/{Uri.EscapeDataString(model)}:generateContent");

            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(attemptTimeoutSeconds);
            try
            {
                using var response = await httpClient.SendAsync(httpRequest, attemptTimeout.Token);
                payload = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(attemptTimeout.Token);
                if (response.IsSuccessStatusCode) break;
                ThrowUnlessRetryable(response.StatusCode, payload?.Error, last);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (last)
                    throw TimedOut(model);
            }

            if (attempt > 0) await Task.Delay(OverloadRetryDelay, cancellationToken);
        }

        var text = payload?.Candidates?
            .SelectMany(candidate => candidate.Content?.Parts ?? [])
            .Select(part => part.Text)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(text))
            throw Unusable(BlockReason(payload));

        var usage = payload?.UsageMetadata;
        return new GeminiTextGenerationResult(
            text,
            model,
            usage?.PromptTokenCount,
            usage?.CandidatesTokenCount,
            payload?.ResponseId);
    }

    public async Task<GeminiTextGenerationResult> GenerateStreamingAsync(
        GeminiTextGenerationRequest request,
        IGeminiTextStreamObserver observer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var parts = BuildParts(request);
        var models = Models(request);
        var attemptTimeoutSeconds = AttemptTimeout(request);
        for (var attempt = 0; ; attempt++)
        {
            var model = models[attempt];
            var last = attempt == models.Length - 1;
            await observer.AttemptStartedAsync(model, attempt > 0, cancellationToken);
            using var httpRequest = CreateRequest(request, parts, $"models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse");

            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(attemptTimeoutSeconds);
            try
            {
                using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, attemptTimeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var failure = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(attemptTimeout.Token);
                    ThrowUnlessRetryable(response.StatusCode, failure?.Error, last);
                }
                else
                {
                    var answer = await ReadStreamAsync(response, observer, cancellationToken, attemptTimeout.Token);
                    if (answer.Error is null) return Completed(answer, model);
                    ThrowUnlessRetryable((HttpStatusCode)(answer.Error.Code ?? (int)HttpStatusCode.InternalServerError), answer.Error, last);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (last)
                    throw TimedOut(model);
            }

            if (attempt > 0) await Task.Delay(OverloadRetryDelay, cancellationToken);
        }
    }

    private static GeminiTextGenerationResult Completed(StreamedAnswer answer, string model)
    {
        var text = answer.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw Unusable(answer.BlockReason);
        return new GeminiTextGenerationResult(
            text,
            model,
            answer.Usage?.PromptTokenCount,
            answer.Usage?.CandidatesTokenCount,
            answer.ResponseId);
    }

    private List<object> BuildParts(GeminiTextGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.IsConfigured)
            throw new InvalidOperationException("Gemini text generation API key is not configured.");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Generation prompt cannot be empty.", nameof(request));

        var parts = new List<object> { new { text = request.Prompt } };
        foreach (var image in request.Images ?? [])
        {
            if (image.Bytes.Length == 0 || image.MimeType is not ("image/png" or "image/jpeg" or "image/webp"))
                throw new ArgumentException("Supply a non-empty PNG, JPEG or WebP image.", nameof(request));
            parts.Add(new { inline_data = new { mime_type = image.MimeType, data = Convert.ToBase64String(image.Bytes) } });
        }
        return parts;
    }

    // Google answers 503, or does not answer at all, while a model is overloaded. Nothing is generated or
    // charged then, so the request moves on: once to the fallback model, then one more try after a pause.
    private string[] Models(GeminiTextGenerationRequest request)
    {
        var primary = request.Model ?? settings.Model;
        var configuredFallback = request.FallbackModel ?? settings.FallbackModel;
        var fallback = string.IsNullOrWhiteSpace(configuredFallback) ? primary : configuredFallback;
        return [primary, fallback, fallback];
    }

    private TimeSpan AttemptTimeout(GeminiTextGenerationRequest request) =>
        request.AttemptTimeout ?? TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds);

    private HttpRequestMessage CreateRequest(GeminiTextGenerationRequest request, List<object> parts, string path)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        httpRequest.Headers.Add("x-goog-api-key", settings.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            systemInstruction = string.IsNullOrWhiteSpace(request.SystemInstruction)
                ? null : new { parts = new[] { new { text = request.SystemInstruction } } },
            contents = new[] { new { role = "user", parts } },
            generationConfig = new
            {
                temperature = request.Temperature ?? settings.Temperature,
                responseMimeType = "application/json",
                responseSchema = request.ResponseSchema
            }
        }, options: RequestJson);
        return httpRequest;
    }

    private static void ThrowUnlessRetryable(HttpStatusCode status, GeminiError? error, bool last)
    {
        if (status != HttpStatusCode.ServiceUnavailable || last)
        {
            throw new HttpRequestException(
                $"Gemini text generation request failed ({(int)status}): "
                + $"{error?.Message ?? "unknown error"}",
                null,
                status);
        }
    }

    private static HttpRequestException TimedOut(string model) =>
        new($"Gemini model {model} did not answer in time.", null, HttpStatusCode.GatewayTimeout);

    // Gemini's own safety filter answers with no text: say so, rather than report an empty answer.
    private static InvalidOperationException Unusable(string? blockReason) =>
        blockReason is not null
            ? new GeminiContentBlockedException(blockReason)
            : new InvalidOperationException("Gemini returned an empty generation.");

    private static async Task<StreamedAnswer> ReadStreamAsync(
        HttpResponseMessage response,
        IGeminiTextStreamObserver observer,
        CancellationToken cancellationToken,
        CancellationToken attemptToken)
    {
        var answer = new StreamedAnswer();
        await using var body = await response.Content.ReadAsStreamAsync(attemptToken);
        using var reader = new StreamReader(body, Encoding.UTF8);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(attemptToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (await DispatchAsync(data, answer, observer, cancellationToken)) return answer;
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            if (data.Length > 0) data.Append('\n');
            data.Append(line.AsSpan(5).TrimStart(' '));
        }
        await DispatchAsync(data, answer, observer, cancellationToken);
        return answer;
    }

    /// <returns>True when the chunk was an error, which ends the stream.</returns>
    private static async Task<bool> DispatchAsync(StringBuilder data, StreamedAnswer answer, IGeminiTextStreamObserver observer, CancellationToken cancellationToken)
    {
        if (data.Length == 0) return false;
        var chunk = JsonSerializer.Deserialize<GenerateContentResponse>(data.ToString());
        data.Clear();
        if (chunk is null) return false;
        if (chunk.Error is not null)
        {
            answer.Error = chunk.Error;
            return true;
        }
        answer.Usage = chunk.UsageMetadata ?? answer.Usage;
        answer.ResponseId ??= chunk.ResponseId;
        answer.BlockReason ??= BlockReason(chunk);
        var text = string.Concat(chunk.Candidates?
            .SelectMany(candidate => candidate.Content?.Parts ?? [])
            .Where(part => part.Thought != true)
            .Select(part => part.Text) ?? []);
        if (text.Length == 0) return false;
        answer.Append(text);
        await observer.TextReceivedAsync(text, cancellationToken);
        return false;
    }

    private static readonly HashSet<string> SafetyFinishReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "SAFETY", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "IMAGE_SAFETY", "IMAGE_PROHIBITED_CONTENT"
    };

    private static string? BlockReason(GenerateContentResponse? payload)
    {
        if (payload?.PromptFeedback?.BlockReason is { Length: > 0 } promptReason) return promptReason;
        return payload?.Candidates?
            .Select(candidate => candidate.FinishReason)
            .FirstOrDefault(reason => reason is not null && SafetyFinishReasons.Contains(reason));
    }

    private sealed record GenerateContentResponse(
        [property: JsonPropertyName("candidates")] Candidate[]? Candidates,
        [property: JsonPropertyName("promptFeedback")] PromptFeedback? PromptFeedback,
        [property: JsonPropertyName("usageMetadata")] UsageMetadata? UsageMetadata,
        [property: JsonPropertyName("responseId")] string? ResponseId,
        [property: JsonPropertyName("error")] GeminiError? Error);

    private sealed record Candidate(
        [property: JsonPropertyName("content")] CandidateContent? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason = null);

    private sealed record PromptFeedback(
        [property: JsonPropertyName("blockReason")] string? BlockReason);

    private sealed record CandidateContent(
        [property: JsonPropertyName("parts")] CandidatePart[]? Parts);

    private sealed record CandidatePart(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("thought")] bool? Thought = null);

    private sealed record UsageMetadata(
        [property: JsonPropertyName("promptTokenCount")] int? PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] int? CandidatesTokenCount);

    private sealed record GeminiError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("code")] int? Code = null);

    private sealed class StreamedAnswer
    {
        private readonly StringBuilder text = new();

        public string Text => text.ToString();

        public UsageMetadata? Usage { get; set; }

        public string? ResponseId { get; set; }

        public string? BlockReason { get; set; }

        public GeminiError? Error { get; set; }

        public void Append(string chunk) => text.Append(chunk);
    }
}
