using System.Net;
using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an immutable audit event for a system resource.
/// </summary>
public sealed class AuditLog : CreationTrackedEntity, ISoftDeletable
{
    private AuditLog()
    {
    }

    /// <summary>
    /// Gets the related seller identifier, when applicable.
    /// </summary>
    public int? SellerId { get; private set; }

    /// <summary>
    /// Gets the related administrator identifier, when applicable.
    /// </summary>
    public int? AdminId { get; private set; }

    /// <summary>
    /// Gets the performed action type.
    /// </summary>
    public string ActionType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the affected resource type.
    /// </summary>
    public string ResourceType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the affected resource identifier.
    /// </summary>
    public int ResourceId { get; private set; }

    /// <summary>
    /// Gets the previous resource value as JSON.
    /// </summary>
    public string? OldValue { get; private set; }

    /// <summary>
    /// Gets the new resource value as JSON.
    /// </summary>
    public string? NewValue { get; private set; }

    /// <summary>
    /// Gets the originating IP address.
    /// </summary>
    public IPAddress? IpAddress { get; private set; }

    /// <summary>
    /// Gets the originating user agent.
    /// </summary>
    public string? UserAgent { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the related administrator, when applicable.
    /// </summary>
    public Admin? Admin { get; private set; }
}
