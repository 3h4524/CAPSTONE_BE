using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for issued authentication tokens.
/// </summary>
public interface IAuthTokenRepository
{
    /// <summary>
    /// Finds a token by its persisted hash.
    /// </summary>
    /// <param name="tokenHash">The hashed token value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching token, or <see langword="null"/> when none exists.</returns>
    Task<AuthToken?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a token to the current unit of work.
    /// </summary>
    /// <param name="authToken">The token to add.</param>
    void Add(AuthToken authToken);
}
