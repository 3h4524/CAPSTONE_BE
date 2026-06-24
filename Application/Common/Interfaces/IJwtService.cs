using APCS.Application.Common.Models;

namespace APCS.Application.Common.Interfaces;

/// <summary>
/// Creates and hashes authentication tokens.
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generates a signed access token.
    /// </summary>
    JwtTokenResult GenerateAccessToken(Guid userId, string email, IReadOnlyCollection<string> roles);

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
}
