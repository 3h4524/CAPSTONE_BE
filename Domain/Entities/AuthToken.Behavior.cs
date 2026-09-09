using APCS.Common.Constants;

namespace APCS.Domain.Entities;

/// <summary>
/// Adds authentication-token behavior without modifying the database-first generated type.
/// </summary>
public partial class AuthToken
{
    public bool IsRevoked => RevokedAt.HasValue;

    public bool IsUsed => UsedAt.HasValue;

    public static AuthToken CreateRefreshToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        string? createdByIp = null,
        string? userAgent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ValidateUserId(userId);

        return Create(
            userId,
            AuthTokenTypes.Refresh,
            tokenHash,
            expiresAtUtc,
            createdAtUtc,
            createdByIp,
            userAgent);
    }

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
        ValidateUserId(userId);

        if (string.Equals(tokenType, AuthTokenTypes.Refresh, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Refresh tokens must be created with CreateRefreshToken.",
                nameof(tokenType));
        }

        return Create(
            userId,
            tokenType,
            tokenHash,
            expiresAtUtc,
            createdAtUtc,
            createdByIp,
            userAgent);
    }

    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAt <= utcNow.UtcDateTime;

    public bool IsActive(DateTimeOffset utcNow) => !IsRevoked && !IsUsed && !IsExpired(utcNow);

    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (!IsUsed)
        {
            UsedAt = usedAtUtc.UtcDateTime;
        }
    }

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        if (!IsRevoked)
        {
            RevokedAt = revokedAtUtc.UtcDateTime;
        }
    }

    private static AuthToken Create(
        Guid userId,
        string tokenType,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        string? createdByIp,
        string? userAgent) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenType = tokenType,
            TokenHash = tokenHash,
            ExpiresAt = expiresAtUtc.UtcDateTime,
            CreatedAt = createdAtUtc.UtcDateTime,
            CreatedByIp = createdByIp,
            UserAgent = userAgent
        };

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier is required.", nameof(userId));
        }
    }
}
