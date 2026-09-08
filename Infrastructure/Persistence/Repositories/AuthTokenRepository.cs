using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists authentication tokens with Entity Framework Core.
/// </summary>
public sealed class AuthTokenRepository(AppDbContext dbContext) : Repository<AuthToken>(dbContext), IAuthTokenRepository
{
    /// <inheritdoc />
    public Task<AuthToken?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return dbContext.AuthTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<AuthToken>> GetRedeemableAsync(
        Guid userId,
        string tokenType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenType);

        // Tracked: the caller revokes what comes back and commits it with the replacement.
        return await dbContext.AuthTokens
            .Where(token => token.UserId == userId
                && token.TokenType == tokenType
                && token.UsedAt == null
                && token.RevokedAt == null)
            .ToArrayAsync(cancellationToken);
    }
}
