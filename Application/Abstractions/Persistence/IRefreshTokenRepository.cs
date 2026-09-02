using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for refresh-token sessions.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Finds a refresh-token session by its persisted token hash.
    /// </summary>
    /// <param name="tokenHash">The hashed refresh-token value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching session, or <see langword="null"/> when no session exists.</returns>
    Task<RefreshToken?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a refresh-token session to the current unit of work.
    /// </summary>
    /// <param name="refreshToken">The session to add.</param>
    void Add(RefreshToken refreshToken);
}
