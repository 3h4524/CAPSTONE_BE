using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Calls Ideogram's image generation API using the Seller's own BYOK key.
/// </summary>
/// <remarks>
/// Each image is requested to run in the background: Ideogram accepts it at once with a generation id, which is asked
/// about until the image is ready, and the finished image is then downloaded from the link it gives. An image is paid
/// for once it is made, so nothing here gives up on one that is still being made because a single call took long, and a
/// download that fails is tried again with a fresh link from the same generation.
/// </remarks>
public sealed class IdeogramImageProvider(IHttpClientFactory clients, IOptions<IdeogramOptions> options) : IImageGenerationProvider
{
    // The size Ideogram lists for each ratio the app offers, at 2K and at 1K. A ratio missing here is left for Ideogram to choose.
    private static readonly Dictionary<string, string> TwoKSizes = new(StringComparer.Ordinal)
    {
        ["1:1"] = "2048x2048",
        ["16:9"] = "2560x1440",
        ["9:16"] = "1440x2560",
        ["4:3"] = "2304x1728",
        ["3:4"] = "1728x2304",
    };

    private static readonly Dictionary<string, string> OneKSizes = new(StringComparer.Ordinal)
    {
        ["1:1"] = "1024x1024",
        ["16:9"] = "1280x720",
        ["9:16"] = "720x1280",
        ["4:3"] = "1152x864",
        ["3:4"] = "864x1152",
    };

    public string Provider => ImageGenerationProviders.Ideogram;

    public bool SupportsTransparentBackground => true;

    public decimal EstimatedCostPerImageUsd => options.Value.CostPerImageUsd;

    public Task<ImageGenerationResult> GenerateAsync(
        string apiKey,
        string prompt,
        string aspectRatio,
        bool transparentBackground = false,
        bool enrichPrompt = false,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var ratio = aspectRatio.Trim();
        var oneK = string.Equals(settings.OutputResolution.Trim(), "1k", StringComparison.OrdinalIgnoreCase);
        var body = new Dictionary<string, object>
        {
            ["prompt"] = prompt,
            ["rendering_speed"] = settings.RenderingSpeed,
            // A prompt with little to go on is filled out by Ideogram's magic prompt, at no extra cost.
            ["magic_prompt"] = enrichPrompt ? "on" : settings.MagicPrompt,
            ["num_images"] = 1,
            ["async"] = true,
            ["enable_copyright_detection"] = settings.CopyrightDetection,
        };

        // The two endpoints size an image differently: the transparent one by ratio and size tier, the other by exact size.
        string model;
        if (transparentBackground)
        {
            model = $"{settings.Model}-transparent";
            body["output_resolution"] = oneK ? "1k" : "2k";
            if (TwoKSizes.ContainsKey(ratio)) body["aspect_ratio"] = ratio.Replace(':', 'x');
        }
        else
        {
            model = settings.Model;
            if ((oneK ? OneKSizes : TwoKSizes).TryGetValue(ratio, out var size)) body["resolution"] = size;
        }

        return GenerateCoreAsync(apiKey, model, JsonSerializer.Serialize(body), cancellationToken);
    }

    // Ideogram's transparent model takes no reference images, so combining images stays with Gemini.
    public Task<ImageGenerationResult> ComposeAsync(
        string apiKey,
        string prompt,
        IReadOnlyList<ImageReference> images,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ImageGenerationResult.Failed("Ideogram is not used to combine images.", options.Value.Model));

    private async Task<ImageGenerationResult> GenerateCoreAsync(string apiKey, string model, string body, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient("Ideogram");
        var started = Stopwatch.GetTimestamp();

        var (accepted, refused) = await SubmitAsync(client, apiKey, model, body, cancellationToken);
        if (refused is not null) return refused;

        // An answer that already holds the image (Ideogram ran it straight away) needs no polling.
        GeneratedImage? image;
        long? costMicros = null;
        if (accepted!.Data is { Count: > 0 } data)
            image = data[0];
        else
        {
            var polled = await PollAsync(client, apiKey, model, accepted.GenerationId!, cancellationToken);
            if (polled.Failure is not null) return polled.Failure;
            image = polled.Image;
            costMicros = polled.CostMicros;
        }
        var generateTime = Stopwatch.GetElapsedTime(started);

        if (image is null)
            return ImageGenerationResult.Failed("Ideogram did not return an image.", model);
        if (!image.IsImageSafe || string.IsNullOrWhiteSpace(image.Url))
            return ImageGenerationResult.Blocked(options.Value.CopyrightDetection
                ? "Generation blocked by Ideogram: the image failed its safety or copyright check (it may show a real brand or character)."
                : "Generation blocked by Ideogram: the image failed its safety check.", model);

        var downloadStarted = Stopwatch.GetTimestamp();
        var download = await DownloadAsync(client, apiKey, model, image.Url, accepted.GenerationId, cancellationToken);
        if (download.Error is not null) return ImageGenerationResult.Failed(download.Error, model);

        var cost = costMicros is long micros ? micros / 1_000_000m : options.Value.CostPerImageUsd;
        return ImageGenerationResult.Success(download.Bytes!, download.MimeType!, model, cost) with
        {
            GenerateTime = generateTime,
            DownloadTime = Stopwatch.GetElapsedTime(downloadStarted),
            Seed = image.Seed,
            ProviderPrompt = image.Prompt
        };
    }

    // Asks Ideogram to make the image. A busy answer is sent again; anything else ends here or hands back the generation.
    private async Task<(Accepted? Accepted, ImageGenerationResult? Refused)> SubmitAsync(
        HttpClient client, string apiKey, string model, string body, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        string payload;
        for (var attempt = 0; ; attempt++)
        {
            // A request can be sent only once, so every attempt builds its own.
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.Endpoint.TrimEnd('/')}/{model}");
            request.Headers.Add("Api-Key", apiKey);
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            try
            {
                response = await client.SendAsync(request, cancellationToken);
                payload = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return (null, ImageGenerationResult.Failed($"Could not reach Ideogram: {ex.Message}", model));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient reports its own timeout as a cancellation, not an HttpRequestException.
                return (null, ImageGenerationResult.Failed("Ideogram did not respond in time (request timed out).", model));
            }

            if (!IsBusy(response.StatusCode, payload) || attempt >= options.Value.MaxRetries)
                break;

            var delay = RetryDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                return (null, ImageGenerationResult.Blocked($"Prompt blocked by Ideogram: {ErrorText(payload)}", model));
            if (RefusesAccount(response.StatusCode, payload))
                return (null, ImageGenerationResult.AccountRefused($"Ideogram returned {(int)response.StatusCode}: {ErrorText(payload)}", model));
            if (!response.IsSuccessStatusCode)
                return (null, ImageGenerationResult.Failed($"Ideogram returned {(int)response.StatusCode}: {ErrorText(payload)}", model));

            Accepted? accepted;
            try
            {
                accepted = JsonSerializer.Deserialize<Accepted>(payload);
            }
            catch (JsonException)
            {
                return (null, ImageGenerationResult.Failed("Ideogram returned an unrecognized response shape.", model));
            }

            return accepted is { GenerationId.Length: > 0 } or { Data.Count: > 0 }
                ? (accepted, null)
                : (null, ImageGenerationResult.Failed("Ideogram did not return an image.", model));
        }
    }

    // Asks about the generation until it is done. Busy answers and dropped connections are waited out a few times in a row.
    private async Task<Polled> PollAsync(HttpClient client, string apiKey, string model, string generationId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var url = $"{settings.GenerationsEndpoint.TrimEnd('/')}/{Uri.EscapeDataString(generationId)}";
        var failures = 0;
        while (true)
        {
            string? trouble;
            TimeSpan? wait = null;
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("Api-Key", apiKey);
                try
                {
                    using var response = await client.SendAsync(request, cancellationToken);
                    var payload = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        trouble = $"Ideogram returned {(int)response.StatusCode} while the image was being made: {ErrorText(payload)}";
                        wait = response.Headers.RetryAfter?.Delta;
                    }
                    else if (!response.IsSuccessStatusCode)
                        return new(null, null, ImageGenerationResult.Failed(
                            $"Ideogram returned {(int)response.StatusCode} while the image was being made: {ErrorText(payload)}", model));
                    else
                    {
                        Generation? generation;
                        try
                        {
                            generation = JsonSerializer.Deserialize<Generation>(payload);
                        }
                        catch (JsonException)
                        {
                            return new(null, null, ImageGenerationResult.Failed("Ideogram returned an unrecognized response shape.", model));
                        }

                        switch (generation?.Status)
                        {
                            case "completed":
                                return new(generation.Data?.FirstOrDefault(), generation.UsageCostUsdMicros, null);
                            case "failed":
                                var reason = generation.FailureReason ?? "no reason given";
                                return new(null, null, reason.Contains("policy", StringComparison.OrdinalIgnoreCase)
                                        || reason.Contains("safety", StringComparison.OrdinalIgnoreCase)
                                    ? ImageGenerationResult.Blocked($"Generation blocked by Ideogram: {reason}", model)
                                    : ImageGenerationResult.Failed($"Ideogram could not make the image: {reason}", model));
                        }

                        // Still being made.
                        failures = 0;
                        trouble = null;
                    }
                }
                catch (HttpRequestException ex)
                {
                    trouble = $"Could not reach Ideogram while the image was being made: {ex.Message}";
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    trouble = "Ideogram did not answer in time while the image was being made.";
                }
            }

            if (trouble is not null && ++failures > settings.MaxPollFailures)
                return new(null, null, ImageGenerationResult.Failed(trouble, model));
            await Task.Delay(wait ?? settings.PollInterval, cancellationToken);
        }
    }

    // Downloads the finished image. A link that cannot be read is replaced by asking the generation for a fresh one.
    private async Task<Download> DownloadAsync(
        HttpClient client, string apiKey, string model, string url, string? generationId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        for (var attempt = 1; ; attempt++)
        {
            var download = await TryDownloadAsync(client, url, cancellationToken);
            if (download.Error is null || attempt >= Math.Max(1, settings.DownloadAttempts)) return download;

            await Task.Delay(settings.RetryBaseDelay, cancellationToken);
            if (generationId is not null
                && await PollAsync(client, apiKey, model, generationId, cancellationToken) is { Image.Url: { Length: > 0 } fresh })
                url = fresh;
        }
    }

    private static async Task<Download> TryDownloadAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            // The link is signed on its own; the API key is not sent to it.
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(null, null, $"Could not download the Ideogram image ({(int)response.StatusCode}).");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var mimeType = response.Content.Headers.ContentType?.MediaType is { Length: > 0 } type && type.StartsWith("image/", StringComparison.Ordinal)
                ? type
                : "image/png";
            return new(bytes, mimeType, null);
        }
        catch (HttpRequestException ex)
        {
            return new(null, null, $"Could not download the Ideogram image: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, null, "The Ideogram image did not download in time.");
        }
    }

    // Too many images at once on the key, Ideogram overloaded or hitting a passing internal error: the same call
    // usually goes through shortly. A daily or plan limit does not clear in seconds, so it is not retried.
    private static bool IsBusy(HttpStatusCode status, string payload) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.InternalServerError
        || (status == HttpStatusCode.TooManyRequests && RejectReason(payload) is null or "inflight_limit");

    // The account itself is turned away (no credits, a key Ideogram no longer accepts, a daily or plan limit): every
    // other image of the job would be refused the same way.
    private static bool RefusesAccount(HttpStatusCode status, string payload) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired
        || (status == HttpStatusCode.TooManyRequests && RejectReason(payload) is not (null or "inflight_limit"));

    // Longer waits are not worth it: one image is given a few minutes in all.
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var delay = response.Headers.RetryAfter?.Delta
            ?? options.Value.RetryBaseDelay * Math.Pow(2, attempt) * (1 + Random.Shared.NextDouble() * 0.2);
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    private static string? RejectReason(string payload) => ReadError(payload)?.RejectReason;

    // Ideogram's own words for a refusal when it sent them, else the raw body.
    private static string ErrorText(string payload) => ReadError(payload) switch
    {
        { Error: { Length: > 0 } error } => error,
        { Message: { Length: > 0 } message } => message,
        _ => payload
    };

    private static IdeogramError? ReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<IdeogramError>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Polled(GeneratedImage? Image, long? CostMicros, ImageGenerationResult? Failure);

    private sealed record Download(byte[]? Bytes, string? MimeType, string? Error);

    // What a generate call answers: the id to poll, or the images when Ideogram made them straight away.
    private sealed record Accepted(
        [property: JsonPropertyName("generation_id")] string? GenerationId,
        [property: JsonPropertyName("data")] IReadOnlyList<GeneratedImage>? Data);

    private sealed record Generation(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("failure_reason")] string? FailureReason,
        [property: JsonPropertyName("usage_cost_usd_micros")] long? UsageCostUsdMicros,
        [property: JsonPropertyName("data")] IReadOnlyList<GeneratedImage>? Data);

    private sealed record GeneratedImage(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("is_image_safe")] bool IsImageSafe,
        [property: JsonPropertyName("prompt")] string? Prompt,
        [property: JsonPropertyName("seed")] long? Seed);

    private sealed record IdeogramError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("reject_reason")] string? RejectReason);
}
