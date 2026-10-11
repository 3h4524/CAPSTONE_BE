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

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"models/{Uri.EscapeDataString(settings.Model)}:generateContent");
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

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Gemini text generation request failed ({(int)response.StatusCode}): "
                + $"{payload?.Error?.Message ?? "unknown error"}");
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
            settings.Model,
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
