using APCS.Common.Constants;
using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a hashed, expiring token issued to a user.
/// </summary>
/// <remarks>
/// Covers refresh tokens as well as single-use password-reset and email-verification tokens.
/// Only the hash is stored; the raw token is never persisted.
/// </remarks>
public sealed class AuthToken : AggregateRoot, IHasCreationTime
{
    private AuthToken()
    {
    }

    private AuthToken(
        Guid userId,
        string tokenType,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        string? jwtId,
        string? createdByIp,
        string? userAgent)
    {
        UserId = userId;
        TokenType = tokenType;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
        JwtId = jwtId;
        CreatedByIp = createdByIp;
        UserAgent = userAgent;
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the token kind.
    /// </summary>
    public string TokenType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the hashed token value.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the JWT identifier of the access token issued alongside a refresh token.
    /// </summary>
    public string? JwtId { get; private set; }

    /// <summary>
    /// Gets the token expiration timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the timestamp at which a single-use token was redeemed.
    /// </summary>
    public DateTimeOffset? UsedAtUtc { get; private set; }

    /// <summary>
    /// Gets the revocation timestamp.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>
    /// Gets the revocation reason.
    /// </summary>
    public string? ReasonRevoked { get; private set; }

    /// <summary>
    /// Gets the hash of the token that replaced this one during rotation.
    /// </summary>
    public string? ReplacedByTokenHash { get; private set; }

    /// <summary>
    /// Gets the IP address the token was issued to.
    /// </summary>
    public string? CreatedByIp { get; private set; }

    /// <summary>
    /// Gets the IP address that revoked the token.
    /// </summary>
    public string? RevokedByIp { get; private set; }

    /// <summary>
    /// Gets the user agent the token was issued to.
    /// </summary>
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Gets the optimistic concurrency token.
    /// </summary>
    public string ConcurrencyStamp { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets a value indicating whether the token has been revoked.
    /// </summary>
    public bool IsRevoked => RevokedAtUtc.HasValue;

    /// <summary>
    /// Gets a value indicating whether a single-use token has been redeemed.
    /// </summary>
    public bool IsUsed => UsedAtUtc.HasValue;

    /// <summary>
    /// Creates a refresh token bound to an issued access token.
    /// </summary>
    public static AuthToken CreateRefreshToken(
        Guid userId,
        string tokenHash,
        string jwtId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        string? createdByIp = null,
        string? userAgent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtId);

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier is required.", nameof(userId));
        }

        return new AuthToken(
            userId,
            AuthTokenTypes.Refresh,
            tokenHash,
            expiresAtUtc,
            createdAtUtc,
            jwtId,
            createdByIp,
            userAgent);
    }

    /// <summary>
    /// Creates a single-use token such as a password reset or email verification token.
    /// </summary>
    public static AuthToken CreateSingleUseToken(
        Guid userId,
        string tokenType,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        string? createdByIp = null,
        string? userAgent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenType);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier is required.", nameof(userId));
        }

        if (tokenType == AuthTokenTypes.Refresh)
        {
            throw new ArgumentException(
                "Refresh tokens must be created with CreateRefreshToken.",
                nameof(tokenType));
        }

        return new AuthToken(
            userId,
            tokenType,
            tokenHash,
            expiresAtUtc,
            createdAtUtc,
            jwtId: null,
            createdByIp,
            userAgent);
    }

    /// <summary>
    /// Returns true when the token has expired.
    /// </summary>
    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAtUtc <= utcNow;

    /// <summary>
    /// Returns true when the token can still be used.
    /// </summary>
    public bool IsActive(DateTimeOffset utcNow) => !IsRevoked && !IsUsed && !IsExpired(utcNow);

    /// <summary>
    /// Marks a single-use token as redeemed.
    /// </summary>
    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (IsUsed)
        {
            return;
        }

        UsedAtUtc = usedAtUtc;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Revokes the token.
    /// </summary>
    public void Revoke(
        DateTimeOffset revokedAtUtc,
        string reason,
        string? replacedByTokenHash = null,
        string? revokedByIp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        ReasonRevoked = reason;
        ReplacedByTokenHash = replacedByTokenHash;
        RevokedByIp = revokedByIp;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
}
