using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Records the attempt to deliver a notification over one channel.
/// </summary>
/// <remarks>
/// Replaces the former channel array so each channel carries its own status, retry count and
/// failure reason.
/// </remarks>
public sealed class NotificationDelivery : CreationTrackedEntity
{
    private NotificationDelivery()
    {
    }

    /// <summary>
    /// Gets the delivered notification identifier.
    /// </summary>
    public Guid NotificationAlertId { get; private set; }

    /// <summary>
    /// Gets the delivery channel.
    /// </summary>
    public string Channel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the delivery status.
    /// </summary>
    public string DeliveryStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets the UTC timestamp when the notification was sent.
    /// </summary>
    public DateTimeOffset? SentAtUtc { get; private set; }

    /// <summary>
    /// Gets the error message, when delivery failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Gets the delivered notification.
    /// </summary>
    public NotificationAlert NotificationAlert { get; private set; } = null!;
}
