using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Infrastructure.Options;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Verifies Google Sign-In ID tokens using Google's published signing keys.
/// </summary>
public sealed class GoogleAuthService(
    IOptions<GoogleAuthOptions> options,
    ILogger<GoogleAuthService> logger)
    : IGoogleAuthService
{
    private readonly GoogleAuthOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<GoogleUserInfoDto?> VerifyIdTokenAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            logger.LogError(
                "Rejected a Google sign-in attempt because {Section}:ClientId is not configured.",
                GoogleAuthOptions.SectionName);
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_options.ClientId]
            };

            // Validates the signature against Google's published keys plus the issuer, audience,
            // and expiry — nothing here is taken on faith from the client.
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            return new GoogleUserInfoDto(
                payload.Subject,
                payload.Email,
                payload.EmailVerified,
                string.IsNullOrWhiteSpace(payload.Name) ? null : payload.Name,
                string.IsNullOrWhiteSpace(payload.Picture) ? null : payload.Picture);
        }
        catch (InvalidJwtException exception)
        {
            logger.LogWarning(exception, "Rejected an invalid Google ID token.");
            return null;
        }
    }
}
