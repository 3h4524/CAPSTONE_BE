using APCS.Common.Constants;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace APCS.IntegrationTests.TestSupport;

/// <summary>
/// Signs access tokens with the production <see cref="JwtService"/> so a test can drive the real
/// bearer authentication handler and role gate.
/// </summary>
/// <remarks>
/// <para>
/// The token is signed with the same issuer, audience, and key
/// <see cref="TestEnvironment"/> publishes to the host, which is what makes the resulting credential
/// acceptable to <c>JwtBearer</c>. Hand-writing the token instead would test a different serializer
/// than production uses and could silently diverge on claim mapping.
/// </para>
/// <para>
/// <see cref="JwtService"/> writes role claims as <see cref="System.Security.Claims.ClaimTypes.Role"/>,
/// which is also the default <c>TokenValidationParameters.RoleClaimType</c>, so a role minted here
/// is compared by the policy handler exactly as a role from a real sign-in would be.
/// </para>
/// </remarks>
public static class TestAuth
{
    /// <summary>The seller role every customer-facing endpoint is gated on.</summary>
    public const string SellerRole = AuthConstants.UserRole;

    /// <summary>The administrator role the admin dashboards and plans are gated on.</summary>
    public const string AdminRole = AuthConstants.AdminRole;

    /// <summary>
    /// The second role accepted by <c>AdminUsersController</c>, which declares its gate as the
    /// literal <c>"Admin,Super Admin"</c> rather than through <see cref="AuthConstants"/>.
    /// </summary>
    public const string SuperAdminRole = "Super Admin";

    private const string TamperedSigningKey = "apcs.integration.test.signing.key.tampered.0123456789";

    /// <summary>
    /// Mints a valid access token carrying the requested roles.
    /// </summary>
    /// <param name="roles">The role claims to embed; an empty list produces a token with no role.</param>
    /// <param name="userId">The subject. A fresh identifier is used when none is supplied.</param>
    /// <param name="email">The email claim.</param>
    /// <returns>A signed bearer token the hosted pipeline accepts.</returns>
    public static string MintToken(
        IReadOnlyCollection<string> roles,
        Guid? userId = null,
        string email = "authz@apcs.test") =>
        CreateService(TimeProvider.System, TestEnvironment.SigningKey)
            .GenerateAccessToken(userId ?? Guid.NewGuid(), email, roles)
            .AccessToken;

    /// <summary>
    /// Mints a correctly signed token whose lifetime has already elapsed.
    /// </summary>
    /// <remarks>
    /// The clock is moved back rather than the lifetime shortened, because
    /// <see cref="JwtOptions.AccessTokenMinutes"/> is validated to stay positive. The offset exceeds
    /// the one-minute <c>ClockSkew</c> configured in <c>Program.cs</c>, so the handler has to reject
    /// the token on lifetime alone.
    /// </remarks>
    /// <param name="roles">The role claims to embed.</param>
    /// <param name="userId">The subject.</param>
    /// <returns>An access token that is already past its expiry.</returns>
    public static string MintExpiredToken(IReadOnlyCollection<string> roles, Guid? userId = null) =>
        CreateService(new BackdatedTimeProvider(TimeSpan.FromHours(-2)), TestEnvironment.SigningKey)
            .GenerateAccessToken(userId ?? Guid.NewGuid(), "expired@apcs.test", roles)
            .AccessToken;

    /// <summary>
    /// Mints a token whose claims are well formed but whose signature does not verify.
    /// </summary>
    /// <remarks>
    /// Issuer, audience, and lifetime stay valid so the only failing check is the signature, which is
    /// what separates this from a malformed-credential test.
    /// </remarks>
    /// <param name="roles">The role claims to embed.</param>
    /// <param name="userId">The subject.</param>
    /// <returns>An access token signed with a key the host does not trust.</returns>
    public static string MintTokenWithWrongSignature(IReadOnlyCollection<string> roles, Guid? userId = null) =>
        CreateService(TimeProvider.System, TamperedSigningKey)
            .GenerateAccessToken(userId ?? Guid.NewGuid(), "tampered@apcs.test", roles)
            .AccessToken;

    private static JwtService CreateService(TimeProvider timeProvider, string signingKey)
    {
        var options = new JwtOptions
        {
            Issuer = TestEnvironment.Get(ConfigurationKeys.Jwt.Issuer)
                ?? throw new InvalidOperationException(
                    "The JWT issuer is not published. Start the host before minting a token."),
            Audience = TestEnvironment.Get(ConfigurationKeys.Jwt.Audience)
                ?? throw new InvalidOperationException(
                    "The JWT audience is not published. Start the host before minting a token."),
            SigningKey = signingKey,
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14
        };

        return new JwtService(Options.Create(options), timeProvider);
    }

    private sealed class BackdatedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }
}
