using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Records a role grant or revocation for audit purposes.
/// </summary>
/// <remarks>
/// Identity deletes the user-role row on revocation, so the trail is kept here instead.
/// </remarks>
public sealed class UserRoleHistory : CreationTrackedEntity
{
    private UserRoleHistory()
    {
    }

    /// <summary>
    /// Gets the affected user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the affected role identifier.
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// Gets the recorded action.
    /// </summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the user who performed the action, when known.
    /// </summary>
    public Guid? PerformedBy { get; private set; }

    /// <summary>
    /// Gets the reason recorded for the action.
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// Creates a history entry.
    /// </summary>
    public static UserRoleHistory Create(Guid userId, Guid roleId, string action, Guid? performedBy, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return new UserRoleHistory
        {
            UserId = userId,
            RoleId = roleId,
            Action = action,
            PerformedBy = performedBy,
            Reason = reason
        };
    }
}
