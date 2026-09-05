using APCS.Domain.Common;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Represents an authorization role.
/// </summary>
/// <remarks>
/// <see cref="IdentityRole{TKey}.Name"/> maps to the <c>code</c> column and is the value used by
/// <c>[Authorize(Roles = ...)]</c>; <see cref="DisplayName"/> is the human-facing label.
/// </remarks>
public sealed class Role : IdentityRole<Guid>, IHasCreationTime
{
    /// <summary>
    /// Initializes a new role with a client-generated identifier.
    /// </summary>
    public Role()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Initializes a new role with the given code.
    /// </summary>
    /// <param name="code">The role code, used for authorization checks.</param>
    public Role(string code)
        : base(code)
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Gets or sets the human-facing role label.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the role ships with the system.
    /// </summary>
    public bool IsSystemRole { get; set; } = true;

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets the permissions granted to this role.
    /// </summary>
    public ICollection<RolePermission> RolePermissions { get; } = new List<RolePermission>();
}
