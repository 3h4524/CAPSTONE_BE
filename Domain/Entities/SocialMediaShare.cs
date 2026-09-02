using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a promotional video share to a social media platform.
/// </summary>
public sealed class SocialMediaShare : CreationTrackedEntity, ISoftDeletable
{
    private SocialMediaShare()
    {
    }

    /// <summary>
    /// Gets the source product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the source promotional video identifier.
    /// </summary>
    public int PromoVideoId { get; private set; }

    /// <summary>
    /// Gets the target social media platform.
    /// </summary>
    public string Platform { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the share status.
    /// </summary>
    public string ShareStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets the scheduled UTC publication timestamp.
    /// </summary>
    public DateTimeOffset? ScheduledTimeUtc { get; private set; }

    /// <summary>
    /// Gets the actual UTC publication timestamp.
    /// </summary>
    public DateTimeOffset? PostedTimeUtc { get; private set; }

    /// <summary>
    /// Gets the post caption.
    /// </summary>
    public string? PostCaption { get; private set; }

    /// <summary>
    /// Gets the post hashtags.
    /// </summary>
    public string[]? Hashtags { get; private set; }

    /// <summary>
    /// Gets the external post identifier.
    /// </summary>
    public string? ExternalPostId { get; private set; }

    /// <summary>
    /// Gets the external platform URL.
    /// </summary>
    public string? ExternalPlatformUrl { get; private set; }

    /// <summary>
    /// Gets the last publication error message.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Gets the external API response as JSON.
    /// </summary>
    public string? ApiResponse { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the source product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source promotional video.
    /// </summary>
    public PromoVideo PromoVideo { get; private set; } = null!;
}
