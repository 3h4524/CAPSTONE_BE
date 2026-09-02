using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a notification delivered to a seller.
/// </summary>
public sealed class NotificationAlert : CreationTrackedEntity, ISoftDeletable
{
    private NotificationAlert()
    {
    }

    /// <summary>
    /// Gets the recipient seller identifier.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the related batch job identifier, when applicable.
    /// </summary>
    public int? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the notification type.
    /// </summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the notification title.
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the notification message.
    /// </summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the notification severity.
    /// </summary>
    public string Severity { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the delivery channels.
    /// </summary>
    public string[] NotificationChannels { get; private set; } = ["in_app"];

    /// <summary>
    /// Gets a value indicating whether the notification has been read.
    /// </summary>
    public bool IsRead { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the notification was read.
    /// </summary>
    public DateTimeOffset? ReadAtUtc { get; private set; }

    /// <summary>
    /// Gets the optional action URL.
    /// </summary>
    public string? ActionUrl { get; private set; }

    /// <summary>
    /// Gets the UTC notification expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the related batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }
}
