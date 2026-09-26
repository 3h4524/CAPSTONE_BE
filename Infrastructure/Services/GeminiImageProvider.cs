using System.Text.Json;
using System.Text.Json.Serialization;
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
    public async Task<ImageGenerationResult> GenerateAsync(
        string apiKey,
        string prompt,
        string aspectRatio,
        CancellationToken cancellationToken = default)
    {
        var model = options.Value.DefaultModel;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);

        // Aspect ratio is folded into the prompt text rather than a structured field: Gemini's
        // image-generation request shape for this has moved between model versions in ways the
        // available docs did not agree on, while a descriptive clause in the prompt is guaranteed
        // to be understood by every version.
        var effectivePrompt = string.IsNullOrWhiteSpace(aspectRatio) || aspectRatio == "1:1"
            ? prompt
            : $"{prompt}\nImage aspect ratio: {aspectRatio}.";

        var body = new GeminiRequest([new GeminiContent([new GeminiPart(effectivePrompt, null)])]);
        request.Content = JsonContent(body);

        var estimatedCost = options.Value.EstimatedCostPerImageUsd;

        HttpResponseMessage response;
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static HttpContent JsonContent(GeminiRequest body) =>
        new StringContent(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json");

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
