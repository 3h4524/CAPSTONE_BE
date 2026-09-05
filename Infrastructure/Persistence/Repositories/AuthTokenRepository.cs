using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists authentication tokens with Entity Framework Core.
/// </summary>
public sealed class AuthTokenRepository(AppDbContext dbContext) : IAuthTokenRepository
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
    public void Add(AuthToken authToken)
    {
        ArgumentNullException.ThrowIfNull(authToken);
        dbContext.AuthTokens.Add(authToken);
    }
}
