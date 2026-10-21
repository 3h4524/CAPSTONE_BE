using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Calls Google's Gemini image generation model ("Nano Banana") using the Seller's own BYOK key.
/// </summary>
/// <remarks>
/// The Gemini <c>generateContent</c> endpoint is synchronous: the generated image comes back inline
/// in the same HTTP response (base64 in <c>candidates[0].content.parts[].inlineData</c>), so unlike a
/// job-based provider (e.g. Replicate) there is no webhook/poll step here — the caller just awaits
/// this call. The model has no "number of images per call" parameter, so one call produces one image;
/// callers wanting N variations must call this N times.
/// </remarks>
public sealed class GeminiImageProvider(IHttpClientFactory clients, IOptions<GeminiOptions> options) : IImageGenerationProvider
{
    public decimal EstimatedCostPerImageUsd => options.Value.EstimatedCostPerImageUsd;

    public Task<ImageGenerationResult> GenerateAsync(
        string apiKey,
        string prompt,
        string aspectRatio,
        CancellationToken cancellationToken = default)
    {
        // Aspect ratio is folded into the prompt text rather than a structured field: Gemini's
        // image-generation request shape for this has moved between model versions in ways the
        // available docs did not agree on, while a descriptive clause in the prompt is guaranteed
        // to be understood by every version. "1:1" must be stated too — Gemini's own default is not
        // necessarily square, so omitting the instruction for it (as an earlier version of this code
        // did) let the model pick whatever ratio it wanted.
        var effectivePrompt = string.IsNullOrWhiteSpace(aspectRatio)
            ? prompt
            : $"{prompt}\nImage aspect ratio: {aspectRatio}.";

        return SendAsync(apiKey, [new GeminiPart(effectivePrompt, null)], cancellationToken);
    }

    public Task<ImageGenerationResult> ComposeAsync(
        string apiKey,
        string prompt,
        IReadOnlyList<ImageReference> images,
        CancellationToken cancellationToken = default) =>
        SendAsync(apiKey,
            [.. images.Select(image => new GeminiPart(null, new GeminiInlineData(image.MimeType, Convert.ToBase64String(image.Bytes)))), new GeminiPart(prompt, null)],
            cancellationToken);

    private async Task<ImageGenerationResult> SendAsync(string apiKey, IReadOnlyList<GeminiPart> parts, CancellationToken cancellationToken)
    {
        var model = options.Value.DefaultModel;
        var body = JsonSerializer.Serialize(new GeminiRequest([new GeminiContent(parts)]), JsonOptions);
        var estimatedCost = options.Value.EstimatedCostPerImageUsd;

        HttpResponseMessage response;
        for (var attempt = 0; ; attempt++)
        {
            // A request can be sent only once, so every attempt builds its own.
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            try
            {
                response = await clients.CreateClient("Gemini").SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return ImageGenerationResult.Failed($"Could not reach Gemini: {ex.Message}", model);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient reports its own timeout as a cancellation, not an HttpRequestException.
                return ImageGenerationResult.Failed("Gemini did not respond in time (request timed out).", model);
            }

            // Busy: the key's rate limit was reached (several images are requested at once) or the model is
            // overloaded. The same call usually goes through a few seconds later.
            if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                || attempt >= options.Value.MaxRetries)
                break;

            var delay = RetryDelay(response, await response.Content.ReadAsStringAsync(cancellationToken), attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ImageGenerationResult.Failed($"Gemini returned {(int)response.StatusCode}: {payload}", model);

            GeminiResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<GeminiResponse>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                return ImageGenerationResult.Failed("Gemini returned an unrecognized response shape.", model);
            }

            if (parsed?.PromptFeedback?.BlockReason is { Length: > 0 } blockReason)
                return ImageGenerationResult.Blocked($"Prompt blocked by Gemini: {blockReason}", model);

            var candidate = parsed?.Candidates?.FirstOrDefault();
            if (candidate is null)
                return ImageGenerationResult.Failed("Gemini returned no candidates.", model);

            var inline = candidate.Content?.Parts?
                .Select(part => part.InlineData)
                .FirstOrDefault(data => data is not null);
            if (inline is { Data.Length: > 0 })
                return ImageGenerationResult.Success(Convert.FromBase64String(inline.Data), inline.MimeType ?? "image/png", model, estimatedCost);

            // No image came back: treat a non-"STOP" finish reason (e.g. "SAFETY", "IMAGE_SAFETY")
            // as a content-safety block rather than a generic failure.
            return candidate.FinishReason is { Length: > 0 } and not "STOP"
                ? ImageGenerationResult.Blocked($"Generation blocked by Gemini: {candidate.FinishReason}", model)
                : ImageGenerationResult.Failed("Gemini did not return an image.", model);
        }
    }

    // A part is either text or an image: the unused one must be left out, not sent as null.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    // Longer waits are not worth it: one image is given three minutes in all.
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    // Gemini puts how long to wait in the error body ("retryDelay": "21s").
    private static readonly Regex RetryDelayPattern = new("\"retryDelay\"\\s*:\\s*\"(\\d+(?:\\.\\d+)?)s\"", RegexOptions.Compiled);

    // The wait Gemini asked for, else a doubling one with a little jitter so images turned away together
    // do not all come back at the same moment.
    private TimeSpan RetryDelay(HttpResponseMessage response, string payload, int attempt)
    {
        var asked = response.Headers.RetryAfter?.Delta
            ?? (RetryDelayPattern.Match(payload) is { Success: true } match
                ? TimeSpan.FromSeconds(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                : (TimeSpan?)null);
        var delay = asked ?? options.Value.RetryBaseDelay * Math.Pow(2, attempt) * (1 + Random.Shared.NextDouble() * 0.2);
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    private sealed record GeminiRequest(
        [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents);

    private sealed record GeminiContent(
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("inlineData")] GeminiInlineData? InlineData);

    private sealed record GeminiInlineData(
        [property: JsonPropertyName("mimeType")] string? MimeType,
        [property: JsonPropertyName("data")] string Data);

    private sealed record GeminiResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates,
        [property: JsonPropertyName("promptFeedback")] GeminiPromptFeedback? PromptFeedback);

    private sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiContent? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason);

    private sealed record GeminiPromptFeedback(
        [property: JsonPropertyName("blockReason")] string? BlockReason);
}
