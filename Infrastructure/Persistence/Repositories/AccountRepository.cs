using APCS.Application.Abstractions.Persistence;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists accounts and their role assignments with Entity Framework Core.
/// </summary>
public sealed class AccountRepository(AppDbContext dbContext) : Repository<User>(dbContext), IAccountRepository
{
    /// <inheritdoc />
    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        return Query().AnyAsync(user => user.Email == normalizedEmail, cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        return Query()
            .Where(user => user.Email == normalizedEmail)
            .OrderBy(user => user.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<User?> FindByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleId);
        return Query().SingleOrDefaultAsync(user => user.OauthGoogleId == googleId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Role?> FindRoleByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return dbContext.Roles.SingleOrDefaultAsync(role => role.Code == code, cancellationToken);
    }

    /// <inheritdoc />
    public void AddRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        dbContext.Roles.Add(role);
    }

    /// <inheritdoc />
    public void AddUserRole(UserRole userRole)
    {
        ArgumentNullException.ThrowIfNull(userRole);
        dbContext.UserRoles.Add(userRole);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> GetActiveRoleCodesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId && userRole.RevokedAt == null)
            .Select(userRole => userRole.Role.Code)
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetActiveUsersByRoleAsync(
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        return await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole =>
                userRole.Role.Code == roleCode &&
                userRole.RevokedAt == null &&
                userRole.User.AccountStatus == AccountStatuses.Active &&
                userRole.User.DeletedAt == null)
            .Select(userRole => userRole.User)
            .Distinct()
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsActiveUserInRoleAsync(
        Guid userId,
        string roleCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        return dbContext.UserRoles
            .AsNoTracking()
            .AnyAsync(userRole =>
                userRole.UserId == userId &&
                userRole.Role.Code == roleCode &&
                userRole.RevokedAt == null &&
                userRole.User.AccountStatus == AccountStatuses.Active &&
                userRole.User.DeletedAt == null,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task TouchLastLoginAsync(
        Guid userId,
        DateTimeOffset lastLoginAtUtc,
        CancellationToken cancellationToken = default)
    {
        var timestamp = lastLoginAtUtc.UtcDateTime;
        return dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.LastLoginAt, timestamp)
                    .SetProperty(user => user.UpdatedAt, timestamp),
                cancellationToken);
    }
}
