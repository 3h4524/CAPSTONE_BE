using System.Net.Http.Json;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.Services;

public sealed class VeoVideoProvider(IHttpClientFactory clients, ILogger<VeoVideoProvider> logger) : IVeoVideoProvider
{
    // Google says why it refused a request in the response body; without it every refusal looks the same.
    private async Task<VeoProviderException> RefusedAsync(HttpResponseMessage response, string step, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Veo refused the {Step} request with status {Status}: {Body}", step, (int)response.StatusCode,
            body.Length > 2000 ? body[..2000] : body);
        return new VeoProviderException((int)response.StatusCode);
    }

    private const string Base = "https://generativelanguage.googleapis.com/v1beta/";
    public async Task<string> StartAsync(string apiKey, string sourceImageUrl, string prompt, string aspectRatio,
        int durationSeconds, bool people, CancellationToken ct)
    {
        if (durationSeconds is not (4 or 6 or 8) || aspectRatio is not ("9:16" or "16:9"))
            throw new ArgumentException("Unsupported Veo video settings.");
        using var source = await clients.CreateClient("PrivateMedia").GetAsync(sourceImageUrl, ct);
        source.EnsureSuccessStatusCode();
        var bytes = await source.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length is < 24 or > 20_000_000 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("AI Shot source must be a valid approved PNG under 20 MB.");
        using var request = new HttpRequestMessage(HttpMethod.Post, Base + "models/veo-3.1-lite-generate-preview:predictLongRunning");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            // The long-running endpoint of this model takes the image this way; it answers 400 to `inlineData`.
            instances = new[] { new { prompt, image = new { bytesBase64Encoded = Convert.ToBase64String(bytes), mimeType = "image/png" } } },
            // The duration goes as a number: the endpoint refuses a string.
            parameters = new { aspectRatio, durationSeconds, resolution = "720p", personGeneration = "allow_adult" }
        });
        using var response = await clients.CreateClient("Veo").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw await RefusedAsync(response, "start", ct);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var name = body.RootElement.TryGetProperty("name", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || !OperationName.IsMatch(name))
        {
            // Google accepted the request, so a clip is being made; without a usable name it cannot be followed.
            logger.LogWarning("Veo accepted the request but named its operation unexpectedly: {Name}", name);
            throw new VeoProviderException(502);
        }

        return name;
    }

    // What the operation of an accepted request is called: "models/<model>/operations/<id>" on this API,
    // or the bare "operations/<id>". Nothing else may be appended to the base address.
    private static readonly System.Text.RegularExpressions.Regex OperationName = new(
        @"^(models/[A-Za-z0-9._-]+/)?operations/[A-Za-z0-9._-]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public async Task<VeoOperationResult> PollAsync(string apiKey, string operationName, CancellationToken ct)
    {
        if (!OperationName.IsMatch(operationName) || operationName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid Veo operation name.");
        using var request = new HttpRequestMessage(HttpMethod.Get, Base + operationName);
        request.Headers.Add("x-goog-api-key", apiKey);
        using var response = await clients.CreateClient("Veo").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw await RefusedAsync(response, "poll", ct);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = body.RootElement;
        if (!root.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True)
            return new(false, null, null);
        if (root.TryGetProperty("error", out var error))
        {
            LogRefusal(operationName, error);
            return new(true, null, error.TryGetProperty("message", out _) ? "Veo could not generate this scene. Adjust the source or prompt." : "Veo could not generate this scene.");
        }
        var uri = root.TryGetProperty("response", out var result) && result.TryGetProperty("generateVideoResponse", out var video) &&
            video.TryGetProperty("generatedSamples", out var samples) && samples.ValueKind == JsonValueKind.Array && samples.GetArrayLength() > 0 &&
            samples[0].TryGetProperty("video", out var item) && item.TryGetProperty("uri", out var link) ? link.GetString() : null;
        // A clip removed by Google's safety filter comes back as a finished operation with no sample, only the reasons.
        if (string.IsNullOrWhiteSpace(uri)) LogRefusal(operationName, root.TryGetProperty("response", out var refused) ? refused : root);
        return string.IsNullOrWhiteSpace(uri) ? new(true, null, "Veo returned no video.") : new(true, uri, null);
    }

    private void LogRefusal(string operationName, JsonElement detail)
    {
        var text = detail.GetRawText();
        logger.LogWarning("Veo finished {Operation} without a video: {Detail}", operationName, text.Length > 2000 ? text[..2000] : text);
    }

    public async Task<byte[]> DownloadAsync(string apiKey, string videoUri, CancellationToken ct)
    {
        if (!Uri.TryCreate(videoUri, UriKind.Absolute, out var uri)) throw new InvalidOperationException("Untrusted Veo video URL.");
        for (var redirect = 0; redirect < 4; redirect++)
        {
            if (uri.Scheme != "https" || uri.Host is not ("generativelanguage.googleapis.com" or "storage.googleapis.com"))
                throw new InvalidOperationException("Untrusted Veo video URL.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("x-goog-api-key", apiKey);
            using var response = await clients.CreateClient("Veo").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 100_000_000) throw new InvalidOperationException("Veo clip exceeds 100 MB.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (memory.Length + count > 100_000_000) throw new InvalidOperationException("Veo clip exceeds 100 MB.");
                await memory.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            var bytes = memory.ToArray();
            if (bytes.Length < 1024 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
                throw new InvalidOperationException("Veo returned an invalid MP4 clip.");
            return bytes;
        }
        throw new InvalidOperationException("Too many Veo download redirects.");
    }
}
