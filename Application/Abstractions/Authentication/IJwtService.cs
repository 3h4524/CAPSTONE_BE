using APCS.Application.Abstractions.Authentication.Dtos;

namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Creates and hashes authentication tokens.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generates a signed access token.
    /// </summary>
    JwtTokenResultDto GenerateAccessToken(Guid userId, string email, IReadOnlyCollection<string> roles);

    /// <summary>
    /// Generates a raw refresh token.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Hashes a raw refresh token for persistence.
    /// </summary>
    string HashRefreshToken(string refreshToken);

    /// <summary>
    /// Gets the refresh token expiration based on the supplied clock value.
    /// </summary>
    DateTimeOffset GetRefreshTokenExpiresAt(DateTimeOffset utcNow);

    /// <summary>
    /// Generates a raw, single-use email verification token.
    /// </summary>
    /// <remarks>
    /// Opaque and random rather than a JWT: the token travels in an emailed link, is stored only
    /// as a hash, and is redeemed against that stored row so it can be expired and revoked.
    /// </remarks>
    string GenerateEmailVerificationToken();

    /// <summary>
    /// Hashes a raw email verification token for persistence and lookup.
    /// </summary>
    string HashEmailVerificationToken(string verificationToken);

    /// <summary>
    /// Gets the email verification token expiration based on the supplied clock value.
    /// </summary>
    DateTimeOffset GetEmailVerificationExpiresAt(DateTimeOffset utcNow);

    /// <summary>
    /// Generates a raw, single-use password reset token.
    /// </summary>
    /// <remarks>
    /// Opaque and random rather than a JWT: the token travels in an emailed link, is stored only
    /// as a hash, and is redeemed against that stored row so it can be expired and revoked.
    /// </remarks>
    string GeneratePasswordResetToken();

    /// <summary>
    /// Hashes a raw password reset token for persistence and lookup.
    /// </summary>
    string HashPasswordResetToken(string resetToken);

    /// <summary>
    /// Gets the password reset token expiration based on the supplied clock value.
    /// </summary>
    DateTimeOffset GetPasswordResetExpiresAt(DateTimeOffset utcNow);
}
