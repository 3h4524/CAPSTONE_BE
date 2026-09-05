using System.Net;
using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an immutable audit event for a system resource.
/// </summary>
/// <remarks>
/// Audit rows are never edited or soft-deleted; that is the point of keeping them.
/// </remarks>
public sealed class AuditLog : CreationTrackedEntity
{
    private AuditLog()
    {
    }

    /// <summary>
    /// Gets the user who performed the action, when known.
    /// </summary>
    public Guid? ActorUserId { get; private set; }

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
    public Guid? ResourceId { get; private set; }

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

    /// <summary>
    /// Creates an audit entry.
    /// </summary>
    public static AuditLog Create(
        string actionType,
        string resourceType,
        Guid? resourceId,
        Guid? actorUserId,
        string? oldValue = null,
        string? newValue = null,
        IPAddress? ipAddress = null,
        string? userAgent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        return new AuditLog
        {
            ActionType = actionType,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ActorUserId = actorUserId,
            OldValue = oldValue,
            NewValue = newValue,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
    }
}
