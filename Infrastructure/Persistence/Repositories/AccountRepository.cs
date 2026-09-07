using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists accounts and their role assignments with Entity Framework Core.
/// </summary>
public sealed class AccountRepository(AppDbContext dbContext) : Repository<User>(dbContext), IAccountRepository
{
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
    public async Task<IReadOnlyCollection<string>> GetActiveRoleCodesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId && userRole.RevokedAt == null)
            .Select(userRole => userRole.Role.Code)
            .ToArrayAsync(cancellationToken);

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
