using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a rendered promotional video for a product.
/// </summary>
public sealed class PromoVideo : SoftDeletableEntity
{
    private readonly List<PromoVideoScene> _scenes = [];
    private readonly List<SocialMediaShare> _shares = [];

    private PromoVideo()
    {
    }

    /// <summary>
    /// Gets the source product identifier.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public Guid? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the video template identifier.
    /// </summary>
    public Guid VideoTemplateId { get; private set; }

    /// <summary>
    /// Gets the background music track identifier, when one was chosen.
    /// </summary>
    public Guid? MusicTrackId { get; private set; }

    /// <summary>
    /// Gets the provider call that rendered this video, when recorded.
    /// </summary>
    public Guid? ApiUsageRecordId { get; private set; }

    /// <summary>
    /// Gets the overlay text.
    /// </summary>
    public string? TextOverlayContent { get; private set; }

    /// <summary>
    /// Gets the overlay text colour, as a hex value.
    /// </summary>
    public string? TextOverlayColor { get; private set; }

    /// <summary>
    /// Gets the overlay text font.
    /// </summary>
    public string? TextOverlayFont { get; private set; }

    /// <summary>
    /// Gets the object-storage provider holding the file.
    /// </summary>
    public string? StorageProvider { get; private set; } = "s3";

    /// <summary>
    /// Gets the object-storage key.
    /// </summary>
    public string? StorageKey { get; private set; }

    /// <summary>
    /// Gets the rendered video URL.
    /// </summary>
    public string? VideoUrl { get; private set; }

    /// <summary>
    /// Gets the video duration in seconds.
    /// </summary>
    public int VideoDurationSeconds { get; private set; }

    /// <summary>
    /// Gets the video resolution.
    /// </summary>
    public string VideoResolution { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the video file format.
    /// </summary>
    public string FileFormat { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the aspect ratio.
    /// </summary>
    public string AspectRatio { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the file size in megabytes.
    /// </summary>
    public decimal? FileSizeMb { get; private set; }

    /// <summary>
    /// Gets the target platform.
    /// </summary>
    public string PlatformTarget { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the computed quality score, on a zero-to-one scale.
    /// </summary>
    public decimal? QualityScore { get; private set; }

    /// <summary>
    /// Gets the optional seller rating, from one to five.
    /// </summary>
    public int? UserRating { get; private set; }

    /// <summary>
    /// Gets the render status.
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
    /// Gets the render duration in seconds.
    /// </summary>
    public decimal? GenerationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets the source product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets the video template used for the render.
    /// </summary>
    public VideoTemplate VideoTemplate { get; private set; } = null!;

    /// <summary>
    /// Gets the background music track, when one was chosen.
    /// </summary>
    public MusicTrack? MusicTrack { get; private set; }

    /// <summary>
    /// Gets the provider call that rendered this video, when recorded.
    /// </summary>
    public ApiUsageRecord? ApiUsageRecord { get; private set; }

    /// <summary>
    /// Gets the ordered scenes making up the video.
    /// </summary>
    public IReadOnlyCollection<PromoVideoScene> Scenes => _scenes.AsReadOnly();

    /// <summary>
    /// Gets the social media shares of this video.
    /// </summary>
    public IReadOnlyCollection<SocialMediaShare> Shares => _shares.AsReadOnly();
}
