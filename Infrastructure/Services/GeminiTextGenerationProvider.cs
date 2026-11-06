using System.Net.Http.Json;
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

        // Google answers 503, or does not answer at all, while a model is overloaded. Nothing is generated or
        // charged then, so the request moves on: once to the fallback model, then one more try after a pause.
        var primary = request.Model ?? settings.Model;
        var configuredFallback = request.FallbackModel ?? settings.FallbackModel;
        var fallback = string.IsNullOrWhiteSpace(configuredFallback) ? primary : configuredFallback;
        string[] models = [primary, fallback, fallback];
        GenerateContentResponse? payload = null;
        var model = primary;
        var attemptTimeoutSeconds = request.AttemptTimeout ?? TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds);
        for (var attempt = 0; ; attempt++)
        {
            model = models[attempt];
            var last = attempt == models.Length - 1;
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"models/{Uri.EscapeDataString(model)}:generateContent");
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

            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(attemptTimeoutSeconds);
            try
            {
                using var response = await httpClient.SendAsync(httpRequest, attemptTimeout.Token);
                payload = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(attemptTimeout.Token);
                if (response.IsSuccessStatusCode) break;
                if (response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable || last)
                {
                    throw new HttpRequestException(
                        $"Gemini text generation request failed ({(int)response.StatusCode}): "
                        + $"{payload?.Error?.Message ?? "unknown error"}",
                        null,
                        response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (last)
                    throw new HttpRequestException($"Gemini model {model} did not answer in time.", null, System.Net.HttpStatusCode.GatewayTimeout);
            }

            if (attempt > 0) await Task.Delay(OverloadRetryDelay, cancellationToken);
        }

        var text = payload?.Candidates?
            .SelectMany(candidate => candidate.Content?.Parts ?? [])
            .Select(part => part.Text)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Gemini returned an empty generation.");

        var usage = payload?.UsageMetadata;
        return new GeminiTextGenerationResult(
            text,
            model,
            usage?.PromptTokenCount,
            usage?.CandidatesTokenCount,
            payload?.ResponseId);
    }

    private sealed record GenerateContentResponse(
        [property: JsonPropertyName("candidates")] Candidate[]? Candidates,
        [property: JsonPropertyName("usageMetadata")] UsageMetadata? UsageMetadata,
        [property: JsonPropertyName("responseId")] string? ResponseId,
        [property: JsonPropertyName("error")] GeminiError? Error);

    private sealed record Candidate(
        [property: JsonPropertyName("content")] CandidateContent? Content);

    private sealed record CandidateContent(
        [property: JsonPropertyName("parts")] CandidatePart[]? Parts);

    private sealed record CandidatePart(
        [property: JsonPropertyName("text")] string? Text);

    private sealed record UsageMetadata(
        [property: JsonPropertyName("promptTokenCount")] int? PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] int? CandidatesTokenCount);

    private sealed record GeminiError(
        [property: JsonPropertyName("message")] string? Message);
}
