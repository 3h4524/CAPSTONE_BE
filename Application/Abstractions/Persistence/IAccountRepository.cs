using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for accounts and their role assignments.
/// </summary>
public interface IAccountRepository : IRepository<User>
{
    /// <summary>
    /// Checks whether a user with the given normalized email already exists.
    /// </summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the oldest user registered with the given normalized email.
    /// </summary>
    /// <remarks>
    /// Oldest wins rather than a single match: the address is protected at registration, but a
    /// duplicate that reached the table another way must not make this account impossible to sign
    /// in to for good.
    /// </remarks>
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a role by its code.
    /// </summary>
    Task<Role?> FindRoleByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a role to the current unit of work.
    /// </summary>
    void AddRole(Role role);

    /// <summary>
    /// Adds a user-role assignment to the current unit of work.
    /// </summary>
    void AddUserRole(UserRole userRole);

    /// <summary>
    /// Gets the codes of every role currently granted to a user.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetActiveRoleCodesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user's last-login timestamp immediately, without staging it on the unit of work.
    /// </summary>
    Task TouchLastLoginAsync(Guid userId, DateTimeOffset lastLoginAtUtc, CancellationToken cancellationToken = default);
}
