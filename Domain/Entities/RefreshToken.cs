using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a persisted refresh token session.
/// </summary>
public sealed class RefreshToken : AggregateRoot
{
    private RefreshToken()
    {
    }

    private RefreshToken(
        int sellerId,
        string tokenHash,
        string jwtId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        SellerId = sellerId;
        TokenHash = tokenHash;
        JwtId = jwtId;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Gets the seller identifier that owns the token.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the hashed refresh token value.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the JWT identifier associated with the access token issued alongside this token.
    /// </summary>
    public string JwtId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the token expiration timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the revocation timestamp.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>
    /// Gets the hash of the token that replaced this one.
    /// </summary>
    public string? ReplacedByTokenHash { get; private set; }

    /// <summary>
    /// Gets the revocation reason.
    /// </summary>
    public string? ReasonRevoked { get; private set; }

    /// <summary>
    /// Gets the optimistic concurrency token.
    /// </summary>
    public string ConcurrencyStamp { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets a value indicating whether the token has been revoked.
    /// </summary>
    public bool IsRevoked => RevokedAtUtc.HasValue;

    /// <summary>
    /// Creates a refresh token entity.
    /// </summary>
    public static RefreshToken Create(
        int sellerId,
        string tokenHash,
        string jwtId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtId);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sellerId);

        return new RefreshToken(sellerId, tokenHash, jwtId, expiresAtUtc, createdAtUtc);
    }

    /// <summary>
    /// Returns true when the token has expired.
    /// </summary>
    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAtUtc <= utcNow;

    /// <summary>
    /// Returns true when the token can still be used.
    /// </summary>
    public bool IsActive(DateTimeOffset utcNow) => !IsRevoked && !IsExpired(utcNow);

    /// <summary>
    /// Revokes the token.
    /// </summary>
    public void Revoke(
        DateTimeOffset revokedAtUtc,
        string reason,
        string? replacedByTokenHash = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        ReasonRevoked = reason;
        ReplacedByTokenHash = replacedByTokenHash;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
}
