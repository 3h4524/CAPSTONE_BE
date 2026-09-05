namespace APCS.Domain.Entities;

/// <summary>
/// Grants a permission to a role.
/// </summary>
/// <remarks>
/// Keyed by (RoleId, PermissionId), so it carries no surrogate identifier. The role side is
/// referenced by identifier only because the role type lives in Infrastructure.
/// </remarks>
public sealed class RolePermission
{
    private RolePermission()
    {
    }

    /// <summary>
    /// Gets the role identifier.
    /// </summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// Gets the permission identifier.
    /// </summary>
    public Guid PermissionId { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the permission was granted to the role.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the granted permission.
    /// </summary>
    public Permission Permission { get; private set; } = null!;
}
