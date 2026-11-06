namespace APCS.Application.Abstractions.VideoQuality;

/// <summary>Provides a signed scene clip and its approved source image to local quality tools.</summary>
public sealed record VideoQualityRegion(double X, double Y, double Width, double Height);
public sealed record VideoQualityInputGeometry(string AspectRatio, VideoQualityRegion? SourceCrop, VideoQualityRegion? Artwork);
public sealed record VideoQualityEvidenceRequest(string ClipUrl, byte[] SourceImage, int SceneOrder,
    byte[]? ReferenceMockup = null, VideoQualityInputGeometry? InputGeometry = null);

/// <summary>Normalized result from configured PaddleOCR and VBench runners.</summary>
public sealed record VideoQualityEvidenceResult(string Status, string EvaluatorVersion,
    IReadOnlyDictionary<string, object?> Metrics, string? Detail = null);

/// <summary>Runs optional local quality tooling without making a video-generation request.</summary>
public interface IVideoQualityEvidenceRunner
{
    Task<VideoQualityEvidenceResult> EvaluateAsync(VideoQualityEvidenceRequest request,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<int, VideoQualityEvidenceResult>> EvaluateBatchAsync(
        IReadOnlyList<VideoQualityEvidenceRequest> requests, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<int, VideoQualityEvidenceResult>();
        foreach (var request in requests)
            results[request.SceneOrder] = await EvaluateAsync(request, cancellationToken);
        return results;
    }
}

/// <summary>Renews a quality job lease while evaluation is running.</summary>
public interface IVideoQualityLeaseHeartbeat
{
    Task RunAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken);
}
