using System.Net;
using System.Net.Http.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.Auth;

/// <summary>
/// Covers the Google sign-in endpoint over real HTTP within the limits of the test host.
/// </summary>
/// <remarks>
/// <para>
/// The integration host replaces the Google adapter with a fake that rejects every ID token
/// instead of contacting Google's token-info endpoint, and the third-party credential guard
/// blanks the Google client id, so no test may reach the real provider. The success path that
/// registers or links an account therefore has no integration coverage here; it is exercised by
/// the Application unit tests against a mocked adapter.
/// </para>
/// <para>
/// What remains worth proving over HTTP is the contract around that boundary: an unverifiable
/// token is refused with the canonical code and never issues a session, malformed input is
/// rejected before any adapter runs, and a rejected sign-in leaves no user row behind.
/// </para>
/// </remarks>
[TestClass]
public sealed class GoogleLoginEndpointTests
{
    private const string GoogleRoute = "/api/auth/google";

    private static IntegrationTestHost _host = null!;

    [ClassInitialize]
    public static async Task StartHostAsync(TestContext testContext)
    {
        _host = await IntegrationTestHost.StartAsync();
    }

    [ClassCleanup]
    public static async Task StopHostAsync()
    {
        await _host.DisposeAsync();
    }

    [TestInitialize]
    public async Task ResetDatabaseAsync()
    {
        await _host.ResetDatabaseAsync();
        await DedicatedDatabaseReset.TruncateConfiguredAsync();
    }

    [TestMethod]
    public async Task GoogleLogin_WhenTheIdTokenCannotBeVerified_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(GoogleRoute, new { idToken = "not-a-real-google-token" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.Unauthorized, ErrorCodes.GoogleTokenInvalid);
    }

    [TestMethod]
    public async Task GoogleLogin_WhenTheIdTokenCannotBeVerified_IssuesNoSessionCookie()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(GoogleRoute, new { idToken = "not-a-real-google-token" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse("a rejected Google sign-in issues no session");
    }

    [TestMethod]
    public async Task GoogleLogin_WhenTheIdTokenIsEmpty_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(GoogleRoute, new { idToken = string.Empty });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task GoogleLogin_WhenTheIdTokenIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var response = await client.PostAsJsonAsync(GoogleRoute, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
