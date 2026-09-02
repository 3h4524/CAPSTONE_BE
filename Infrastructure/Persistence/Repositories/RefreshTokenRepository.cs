using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists refresh-token sessions with Entity Framework Core.
/// </summary>
public sealed class RefreshTokenRepository(AppDbContext dbContext) : IRefreshTokenRepository
{
    /// <inheritdoc />
    public Task<RefreshToken?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return dbContext.RefreshTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(RefreshToken refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        dbContext.RefreshTokens.Add(refreshToken);
    }
}
