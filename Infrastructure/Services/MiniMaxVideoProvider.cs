using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.AI;
using APCS.Application.Features.Workflows;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace APCS.Infrastructure.Services;

/// <summary>MiniMax H3 image-to-video adapter for newly planned AI video scenes.</summary>
public sealed class MiniMaxVideoProvider(IHttpClientFactory clients, ILogger<MiniMaxVideoProvider> logger)
    : IAiVideoGenerationStrategy
{
    private const string BaseUrl = "https://api.minimax.io";
    private const int MaxSourceBytes = 20_000_000;
    private const int MaxPreparedBytes = 30_000_000;
    private const int MaxClipBytes = 100_000_000;
    private static readonly Regex TaskId = new("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant);

    public string ProviderKey => AiVideoModel.ProviderKey;
    public string ModelKey => AiVideoModel.ModelKey;
    public string ModelName => AiVideoModel.ModelName;

    public Task<string> StartAsync(string apiKey, string sourceImageUrl, string prompt, string aspectRatio,
        int durationSeconds, bool people, CancellationToken ct) =>
        StartAsync(apiKey, sourceImageUrl, prompt, aspectRatio, durationSeconds, people, null, ct);

    public async Task<string> StartAsync(string apiKey, string sourceImageUrl, string prompt, string aspectRatio,
        int durationSeconds, bool people, Region? sourceCrop, CancellationToken ct)
    {
        byte[] firstFrame;
        try
        {
            ValidateSettings(prompt, aspectRatio, durationSeconds);
            firstFrame = await PrepareFirstFrameAsync(sourceImageUrl, aspectRatio, sourceCrop, ct);
        }
        catch (Exception ex) when (ex is not AiVideoNotSubmittedException)
        {
            var message = ex is OperationCanceledException && ct.IsCancellationRequested
                ? "Stopped before the request was sent."
                : ex is TaskCanceledException
                    ? "Timed out reading the approved source image."
                    : $"Could not prepare the approved source image: {ex.Message}";
            throw new AiVideoNotSubmittedException(message, ex);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v2/video_generation");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model = ModelName,
            content = new object[]
            {
                new { type = "text", text = prompt },
                new
                {
                    type = "image_url",
                    image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(firstFrame) },
                    role = "first_frame"
                }
            },
            resolution = AiVideoModel.Resolution,
            duration = durationSeconds,
            // H3 derives image-to-video ratio from the prepared first frame.
            ratio = "adaptive"
        });

        HttpResponseMessage sent;
        try
        {
            sent = await clients.CreateClient("MiniMax").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError)
        {
            throw new AiVideoNotSubmittedException($"Could not reach MiniMax: {ex.Message}", ex);
        }

        using var response = sent;
        if (!response.IsSuccessStatusCode) throw await RefusedAsync(response, "start", ct);

        string? taskId;
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            taskId = GetString(body.RootElement, "task_id");
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            throw new AiVideoSubmissionUnknownException("MiniMax accepted the request, but its answer could not be read.", ex);
        }

        if (string.IsNullOrWhiteSpace(taskId) || !TaskId.IsMatch(taskId) || taskId.Contains("..", StringComparison.Ordinal))
        {
            logger.LogWarning("MiniMax accepted the request but returned an unexpected task id: {TaskId}", taskId);
            throw new AiVideoSubmissionUnknownException("MiniMax accepted the request, but did not return a task id that can be followed.");
        }

        return taskId;
    }

    public async Task<VeoOperationResult> PollAsync(string apiKey, string operationName, CancellationToken ct)
    {
        if (!TaskId.IsMatch(operationName) || operationName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid MiniMax task id.", nameof(operationName));

        using var request = new HttpRequestMessage(HttpMethod.Get,
            BaseUrl + "/v2/query/video_generation/" + Uri.EscapeDataString(operationName));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await clients.CreateClient("MiniMax")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw await RefusedAsync(response, "poll", ct);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (body.RootElement.ValueKind != JsonValueKind.Object ||
            !body.RootElement.TryGetProperty("task", out var task) || task.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("MiniMax returned an invalid task response.");
        if (!string.Equals(GetString(task, "id"), operationName, StringComparison.Ordinal) ||
            !string.Equals(GetString(task, "model"), ModelName, StringComparison.Ordinal))
            throw new InvalidOperationException("MiniMax returned a different task or model than requested.");

        var status = GetString(task, "status")?.ToLowerInvariant();
        if (status is "queued" or "running") return new(false, null, null);
        if (status == "succeeded")
        {
            if (!task.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object ||
                string.IsNullOrWhiteSpace(GetString(content, "url")))
                throw new InvalidOperationException("MiniMax completed the task without a video URL.");
            return new(true, GetString(content, "url"), null);
        }
        if (status is "failed" or "cancelled")
        {
            string? failure = status == "cancelled" ? "MiniMax cancelled the video task." : "MiniMax could not generate the video.";
            if (task.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = GetString(error, "code");
                var detail = Shorten(GetString(error, "message"));
                if (!string.IsNullOrWhiteSpace(detail)) failure = string.IsNullOrWhiteSpace(code) ? detail : $"{detail} ({code})";
            }
            return new(true, null, failure);
        }

        throw new InvalidOperationException("MiniMax returned an unknown task status.");
    }

    public async Task<byte[]> DownloadAsync(string apiKey, string videoUri, CancellationToken ct)
    {
        if (!Uri.TryCreate(videoUri, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Untrusted MiniMax video URL.");

        for (var redirect = 0; redirect < 4; redirect++)
        {
            EnsureTrustedVideoUri(uri);
            // The output URL is temporary and self-authorizing. Never send the seller's API key to a CDN.
            using var response = await clients.CreateClient("MiniMax")
                .SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next = response.Headers.Location ?? throw new InvalidOperationException("MiniMax returned an invalid video redirect.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            if (!response.IsSuccessStatusCode) throw await RefusedAsync(response, "download", ct);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = await ReadLimitedAsync(stream, MaxClipBytes, "MiniMax clip exceeds 100 MB.", ct);
            if (bytes.Length < 1024 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
                throw new InvalidOperationException("MiniMax returned an invalid MP4 clip.");
            return bytes;
        }

        throw new InvalidOperationException("Too many MiniMax download redirects.");
    }

    private async Task<byte[]> PrepareFirstFrameAsync(string sourceImageUrl, string aspectRatio, Region? sourceCrop,
        CancellationToken ct)
    {
        using var response = await clients.CreateClient("PrivateMedia")
            .GetAsync(sourceImageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
        var source = await ReadLimitedAsync(sourceStream, MaxSourceBytes,
            "AI video source exceeds 20 MB.", ct);
        if (source.Length < 24 || !source.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("AI video source must be a valid approved PNG under 20 MB.");

        using var image = Image.Load(source);
        if (sourceCrop is not null) image.Mutate(x => x.Crop(ToPixelCrop(sourceCrop, image.Width, image.Height)));
        image.Mutate(x => x.Crop(CentreCrop(image.Width, image.Height, aspectRatio)));

        var scale = Math.Min(1d, 5760d / Math.Max(image.Width, image.Height));
        if (Math.Min(image.Width, image.Height) * scale < 256)
            scale = 256d / Math.Min(image.Width, image.Height);
        if (Math.Abs(scale - 1d) > 0.0001)
            image.Mutate(x => x.Resize(Math.Max(1, (int)Math.Round(image.Width * scale)),
                Math.Max(1, (int)Math.Round(image.Height * scale))));
        if (image.Width > 5760 || image.Height > 5760)
            throw new InvalidOperationException("The approved source crop is outside MiniMax image limits.");

        await using var prepared = new MemoryStream();
        await image.SaveAsPngAsync(prepared, ct);
        if (prepared.Length > MaxPreparedBytes)
            throw new InvalidOperationException("The prepared MiniMax first frame exceeds 30 MB.");
        return prepared.ToArray();
    }

    private async Task<AiVideoProviderException> RefusedAsync(HttpResponseMessage response, string step, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("MiniMax refused the {Step} request with status {Status}: {Body}", step,
            (int)response.StatusCode, body.Length > 2000 ? body[..2000] : body);
        string? detail = null;
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object &&
                parsed.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                detail = Shorten(GetString(error, "message"));
        }
        catch (JsonException) { /* Some provider/CDN refusals have a non-JSON body. */ }
        return new AiVideoProviderException((int)response.StatusCode, detail);
    }

    private static void ValidateSettings(string prompt, string aspectRatio, int durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 7000)
            throw new ArgumentException("MiniMax prompt must contain 1 to 7000 characters.", nameof(prompt));
        if (durationSeconds is not (4 or 6 or 8) || aspectRatio is not ("9:16" or "16:9"))
            throw new ArgumentException("Unsupported MiniMax video settings.");
    }

    private static Rectangle ToPixelCrop(Region crop, int width, int height)
    {
        var x = Math.Clamp((int)Math.Floor(crop.X * width), 0, width - 1);
        var y = Math.Clamp((int)Math.Floor(crop.Y * height), 0, height - 1);
        var cropWidth = Math.Clamp((int)Math.Ceiling(crop.Width * width), 1, width - x);
        var cropHeight = Math.Clamp((int)Math.Ceiling(crop.Height * height), 1, height - y);
        return new Rectangle(x, y, cropWidth, cropHeight);
    }

    private static Rectangle CentreCrop(int width, int height, string aspectRatio)
    {
        var targetRatio = aspectRatio == "9:16" ? 9d / 16d : 16d / 9d;
        var currentRatio = (double)width / height;
        if (currentRatio > targetRatio)
        {
            var targetWidth = Math.Max(1, (int)Math.Round(height * targetRatio));
            return new Rectangle((width - targetWidth) / 2, 0, targetWidth, height);
        }
        var targetHeight = Math.Max(1, (int)Math.Round(width / targetRatio));
        return new Rectangle(0, (height - targetHeight) / 2, width, targetHeight);
    }

    private static void EnsureTrustedVideoUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !IsTrustedHost(uri.Host))
            throw new InvalidOperationException("Untrusted MiniMax video URL.");
    }

    private static bool IsTrustedHost(string host) =>
        IsDomainOrSubdomain(host, "minimax.io") ||
        IsDomainOrSubdomain(host, "minimax.chat") ||
        IsDomainOrSubdomain(host, "hailuoai.com");

    private static bool IsDomainOrSubdomain(string host, string domain) =>
        string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    private static string? Shorten(string? text)
    {
        var line = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length == 0 ? null : line.Length > 400 ? line[..400] + "…" : line;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, string message, CancellationToken ct)
    {
        await using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + count > limit) throw new InvalidOperationException(message);
            await memory.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return memory.ToArray();
    }
}
