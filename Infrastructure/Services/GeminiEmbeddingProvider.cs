using System.Net.Http.Json;
using System.Text.Json.Serialization;
using APCS.Application.Abstractions.Rag;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class GeminiEmbeddingProvider(
    HttpClient httpClient,
    IOptions<GeminiEmbeddingOptions> options) : IRagEmbeddingProvider
{
    private readonly GeminiEmbeddingOptions settings = options.Value;

    public string ModelName => settings.Model;

    public int Dimensions => settings.Dimensions;

    public async Task<float[]> CreateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured)
            throw new InvalidOperationException("Gemini embedding API key is not configured.");

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Embedding input cannot be empty.", nameof(text));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"models/{Uri.EscapeDataString(settings.Model)}:embedContent");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            content = new
            {
                parts = new[] { new { text } }
            },
            outputDimensionality = settings.Dimensions
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Gemini embeddings request failed ({(int)response.StatusCode}): "
                + $"{payload?.Error?.Message ?? "unknown error"}");
        }

        var values = payload?.Embedding?.Values
            ?? payload?.Embeddings?.FirstOrDefault()?.Values;

        if (values is null || values.Length == 0)
            throw new InvalidOperationException("Gemini returned an empty embedding.");

        if (values.Length != settings.Dimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimension mismatch. Expected {settings.Dimensions}, received {values.Length}.");
        }

        return values;
    }

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("embedding")] EmbeddingValues? Embedding,
        [property: JsonPropertyName("embeddings")] EmbeddingValues[]? Embeddings,
        [property: JsonPropertyName("error")] GeminiError? Error);

    private sealed record EmbeddingValues(
        [property: JsonPropertyName("values")] float[]? Values);

    private sealed record GeminiError(
        [property: JsonPropertyName("message")] string? Message);
}
