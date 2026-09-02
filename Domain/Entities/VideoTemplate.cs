using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a reusable template for promotional video generation.
/// </summary>
public sealed class VideoTemplate : AuditableEntity
{
    private readonly List<PromoVideo> _promoVideos = [];

    private VideoTemplate()
    {
    }

    /// <summary>
    /// Gets the template name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the template type.
    /// </summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the target platform.
    /// </summary>
    public string Platform { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the output duration in seconds.
    /// </summary>
    public int DurationSeconds { get; private set; }

    /// <summary>
    /// Gets the output aspect ratio.
    /// </summary>
    public string AspectRatio { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the output resolution.
    /// </summary>
    public string Resolution { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the effects configuration as JSON.
    /// </summary>
    public string EffectsConfig { get; private set; } = "{}";

    /// <summary>
    /// Gets the preview video URL.
    /// </summary>
    public string? PreviewVideoUrl { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this is a system template.
    /// </summary>
    public bool IsSystemTemplate { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the template is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the videos generated with this template.
    /// </summary>
    public IReadOnlyCollection<PromoVideo> PromoVideos => _promoVideos.AsReadOnly();
}
