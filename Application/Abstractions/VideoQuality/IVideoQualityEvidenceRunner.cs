namespace APCS.Application.Abstractions.VideoQuality;

/// <summary>Provides a signed scene clip and its approved source image to local quality tools.</summary>
public sealed record VideoQualityEvidenceRequest(string ClipUrl, byte[] SourceImage, int SceneOrder);

/// <summary>Normalized result from configured PaddleOCR and VBench runners.</summary>
public sealed record VideoQualityEvidenceResult(string Status, string EvaluatorVersion,
    IReadOnlyDictionary<string, object?> Metrics, string? Detail = null);

/// <summary>Runs optional local quality tooling without making a video-generation request.</summary>
public interface IVideoQualityEvidenceRunner
{
    Task<VideoQualityEvidenceResult> EvaluateAsync(VideoQualityEvidenceRequest request,
        CancellationToken cancellationToken = default);
}
