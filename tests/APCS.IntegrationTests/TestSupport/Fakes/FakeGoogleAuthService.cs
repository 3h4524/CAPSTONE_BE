using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;

namespace APCS.IntegrationTests.TestSupport.Fakes;

/// <summary>
/// Rejects every Google ID token instead of contacting Google's token-info endpoint.
/// </summary>
public sealed class FakeGoogleAuthService : IGoogleAuthService
{
    /// <inheritdoc />
    public Task<GoogleUserInfoDto?> VerifyIdTokenAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        return Task.FromResult<GoogleUserInfoDto?>(null);
    }
}
