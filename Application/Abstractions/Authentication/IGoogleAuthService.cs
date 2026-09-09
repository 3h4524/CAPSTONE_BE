using APCS.Application.Abstractions.Authentication.Dtos;

namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Verifies Google Sign-In ID tokens.
/// </summary>
public interface IGoogleAuthService
{
    /// <summary>
    /// Verifies a Google ID token's signature, issuer, audience, and expiry.
    /// </summary>
    /// <returns>The identity Google vouched for, or <see langword="null"/> when the token is invalid.</returns>
    Task<GoogleUserInfoDto?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken = default);
}
