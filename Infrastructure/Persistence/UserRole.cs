using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Links a user to a role.
/// </summary>
/// <remarks>
/// Identity's <c>UserStore</c> treats the (UserId, RoleId) pair as the key: granting inserts a row
/// and revoking deletes it. Revocation history therefore lives in <c>UserRoleHistory</c> rather
/// than as a nullable revoked-at column here, which would otherwise leave revoked rows looking
/// active to Identity and block re-granting the same role.
/// </remarks>
public sealed class UserRole : IdentityUserRole<Guid>
{
    /// <summary>
    /// Gets or sets the UTC timestamp when the role was granted.
    /// </summary>
    public DateTimeOffset GrantedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the user who granted the role, when known.
    /// </summary>
    public Guid? GrantedBy { get; set; }
}
