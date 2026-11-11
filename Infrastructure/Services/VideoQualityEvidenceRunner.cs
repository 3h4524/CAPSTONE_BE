using System.Net.Http.Json;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>Sends scene batches to the separate Python evaluator when configured.</summary>
public sealed class VideoQualityEvidenceRunner(IOptions<VideoQualityOptions> options, IHttpClientFactory clients,
    ProcessVideoQualityEvidenceRunner legacy) : IVideoQualityEvidenceRunner
{
    public async Task<VideoQualityEvidenceResult> EvaluateAsync(VideoQualityEvidenceRequest request, CancellationToken cancellationToken = default) =>
        (await EvaluateBatchAsync([request], cancellationToken))[request.SceneOrder];

    public async Task<IReadOnlyDictionary<int, VideoQualityEvidenceResult>> EvaluateBatchAsync(
        IReadOnlyList<VideoQualityEvidenceRequest> requests, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.WorkerUrl))
        {
            var results = new Dictionary<int, VideoQualityEvidenceResult>();
            foreach (var request in requests) results[request.SceneOrder] = await legacy.EvaluateAsync(request, cancellationToken);
            return results;
        }
        if (string.IsNullOrWhiteSpace(settings.WorkerToken))
            throw new InvalidOperationException("VideoQuality:WorkerToken is required when using a quality worker.");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.WorkerUrl.TrimEnd('/') + "/"), "evaluate"));
        requestMessage.Headers.Add("X-Worker-Token", settings.WorkerToken);
        requestMessage.Content = JsonContent.Create(new { scenes = requests.Select(item => new
        {
            sceneOrder = item.SceneOrder, clipUrl = item.ClipUrl, sourceImage = Convert.ToBase64String(item.SourceImage),
            referenceMockup = item.ReferenceMockup is null ? null : Convert.ToBase64String(item.ReferenceMockup),
            inputGeometry = item.InputGeometry
        }) });
        // The Python HTTP server requires Content-Length and does not decode chunked bodies.
        // Buffering also enforces its 128 MiB request limit before anything is sent.
        await requestMessage.Content.LoadIntoBufferAsync(128L * 1024 * 1024).WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.WorkerTimeoutSeconds, 30, 14400)));
        using var response = await clients.CreateClient("VideoQualityWorker").SendAsync(requestMessage, timeout.Token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BatchResponse>(cancellationToken: timeout.Token)
            ?? throw new InvalidOperationException("Video quality worker returned an empty response.");
        return body.Results.ToDictionary(item => item.SceneOrder,
            item => new VideoQualityEvidenceResult(item.Status, item.EvaluatorVersion, item.Metrics, item.Detail));
    }

    private sealed record BatchResponse(IReadOnlyList<SceneResult> Results);
    private sealed record SceneResult(int SceneOrder, string Status, string EvaluatorVersion,
        IReadOnlyDictionary<string, object?> Metrics, string? Detail);
}
