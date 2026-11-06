using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using APCS.Application.Abstractions.Rag;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class GeminiEmbeddingProvider(
    HttpClient httpClient,
    IOptions<GeminiEmbeddingOptions> options,
    IMemoryCache cache) : IRagEmbeddingProvider
{
    private const int MaxAttempts = 3;
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

        var cacheKey = $"rag-embedding:{settings.Model}:{settings.Dimensions}:"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (cache.TryGetValue(cacheKey, out var cachedObject) && cachedObject is float[] cached)
            return cached.ToArray();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
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

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var values = payload?.Embedding?.Values
                        ?? payload?.Embeddings?.FirstOrDefault()?.Values;

                    if (values is null || values.Length == 0)
                        throw new InvalidOperationException("Gemini returned an empty embedding.");

                    if (values.Length != settings.Dimensions)
                    {
                        throw new InvalidOperationException(
                            $"Embedding dimension mismatch. Expected {settings.Dimensions}, received {values.Length}.");
                    }

                    cache.Set(cacheKey, values, TimeSpan.FromHours(6));
                    return values.ToArray();
                }

                if (attempt < MaxAttempts && IsTransient(response.StatusCode))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), cancellationToken);
                    continue;
                }

                throw new HttpRequestException(
                    $"Gemini embeddings request failed ({(int)response.StatusCode}): "
                    + $"{payload?.Error?.Message ?? "unknown error"}");
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }

        throw new InvalidOperationException("Gemini embedding request did not complete.");
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("embedding")] EmbeddingValues? Embedding,
        [property: JsonPropertyName("embeddings")] EmbeddingValues[]? Embeddings,
        [property: JsonPropertyName("error")] GeminiError? Error);

    private sealed record EmbeddingValues(
        [property: JsonPropertyName("values")] float[]? Values);

    private sealed record GeminiError(
        [property: JsonPropertyName("message")] string? Message);
}
