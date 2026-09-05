using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a single authorization permission granted through roles.
/// </summary>
public sealed class Permission : CreationTrackedEntity
{
    private readonly List<RolePermission> _rolePermissions = [];

    private Permission()
    {
    }

    /// <summary>
    /// Gets the permission code, such as <c>product.create</c>.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the resource the permission applies to.
    /// </summary>
    public string Resource { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the action allowed on the resource.
    /// </summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the permission description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the roles this permission is assigned to.
    /// </summary>
    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions.AsReadOnly();
}
