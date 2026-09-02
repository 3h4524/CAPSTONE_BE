using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a promotional video generated for a product.
/// </summary>
public sealed class PromoVideo : SoftDeletableEntity
{
    private PromoVideo()
    {
    }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public int? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the video template identifier.
    /// </summary>
    public int VideoTemplateId { get; private set; }

    /// <summary>
    /// Gets the identifiers of design images used by the video.
    /// </summary>
    public int[] DesignImageIds { get; private set; } = [];

    /// <summary>
    /// Gets the music track identifier, when applicable.
    /// </summary>
    public int? MusicTrackId { get; private set; }

    /// <summary>
    /// Gets the text overlay content.
    /// </summary>
    public string? TextOverlayContent { get; private set; }

    /// <summary>
    /// Gets the text overlay color.
    /// </summary>
    public string? TextOverlayColor { get; private set; }

    /// <summary>
    /// Gets the text overlay font.
    /// </summary>
    public string? TextOverlayFont { get; private set; }

    /// <summary>
    /// Gets the generated video URL.
    /// </summary>
    public string? VideoUrl { get; private set; }

    /// <summary>
    /// Gets the optional local video path.
    /// </summary>
    public string? VideoLocalPath { get; private set; }

    /// <summary>
    /// Gets the generated video duration in seconds.
    /// </summary>
    public int VideoDurationSeconds { get; private set; }

    /// <summary>
    /// Gets the generated video resolution.
    /// </summary>
    public string VideoResolution { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the video file format.
    /// </summary>
    public string FileFormat { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the generated video aspect ratio.
    /// </summary>
    public string AspectRatio { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the file size in megabytes.
    /// </summary>
    public decimal? FileSizeMb { get; private set; }

    /// <summary>
    /// Gets the target publishing platform.
    /// </summary>
    public string PlatformTarget { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the computed quality score.
    /// </summary>
    public decimal? QualityScore { get; private set; }

    /// <summary>
    /// Gets the optional seller rating.
    /// </summary>
    public int? UserRating { get; private set; }

    /// <summary>
    /// Gets the generation status.
    /// </summary>
    public string Status { get; private set; } = "pending";

    /// <summary>
    /// Gets the approval status.
    /// </summary>
    public string ApprovalStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets a value indicating whether this is the final video.
    /// </summary>
    public bool IsFinal { get; private set; }

    /// <summary>
    /// Gets the generation duration in seconds.
    /// </summary>
    public decimal? GenerationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets the generation API cost in USD.
    /// </summary>
    public decimal ApiCostUsd { get; private set; }

    /// <summary>
    /// Gets the target product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets the video template.
    /// </summary>
    public VideoTemplate VideoTemplate { get; private set; } = null!;

    /// <summary>
    /// Gets the music track, when applicable.
    /// </summary>
    public MusicTrack? MusicTrack { get; private set; }
}
