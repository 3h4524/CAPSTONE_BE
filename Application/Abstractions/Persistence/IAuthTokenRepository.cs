using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for issued authentication tokens.
/// </summary>
public interface IAuthTokenRepository : IRepository<AuthToken>
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
    /// Finds the tokens of one kind that a user can still redeem.
    /// </summary>
    /// <param name="userId">The owning user.</param>
    /// <param name="tokenType">The token kind to look for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <remarks>
    /// The database allows only one unused, unrevoked token per user and kind, so a caller that
    /// issues a replacement must revoke what it finds here first. Expiry is not part of that
    /// database rule, so an expired token still occupies the slot and is returned too.
    /// </remarks>
    Task<IReadOnlyCollection<AuthToken>> GetRedeemableAsync(
        Guid userId,
        string tokenType,
        CancellationToken cancellationToken = default);
}
