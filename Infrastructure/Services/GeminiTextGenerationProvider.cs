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

    private readonly GeminiTextGenerationOptions settings = options.Value;

    public async Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.IsConfigured)
            throw new InvalidOperationException("Gemini text generation API key is not configured.");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Generation prompt cannot be empty.", nameof(request));

        var contents = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemInstruction))
        {
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = request.SystemInstruction } }
            });
        }

        contents.Add(new
        {
            role = "user",
            parts = new[] { new { text = request.Prompt } }
        });

        // Google answers 503, or does not answer at all, while a model is overloaded. Nothing is generated or
        // charged then, so the request moves on: once to the fallback model, then one more try after a pause.
        var fallback = string.IsNullOrWhiteSpace(settings.FallbackModel) ? settings.Model : settings.FallbackModel;
        string[] models = [settings.Model, fallback, fallback];
        GenerateContentResponse? payload = null;
        var model = settings.Model;
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
                contents,
                generationConfig = new
                {
                    temperature = settings.Temperature,
                    responseMimeType = "application/json"
                }
            });

            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds));
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
