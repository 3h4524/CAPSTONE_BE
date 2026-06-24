using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Common.Interfaces;

/// <summary>
/// Exposes application persistence operations required by use cases.
/// </summary>
public interface IAppDbContext
{
    /// <summary>
    /// Gets refresh token sessions.
    /// </summary>
    DbSet<RefreshToken> RefreshTokens { get; }

    /// <summary>
    /// Begins a database transaction for operations that must be atomic.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The active transaction.</returns>
    Task<IAppDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists pending changes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of written state entries.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
