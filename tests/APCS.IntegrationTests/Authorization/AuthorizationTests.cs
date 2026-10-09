using System.Net;
using System.Net.Http.Headers;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.Authorization;

/// <summary>
/// Covers the authorization gate across the real middleware pipeline.
/// </summary>
/// <remarks>
/// Every production controller except <c>AuthController</c>, <c>PayOsWebhookController</c>, and the
/// development-only endpoints sits behind <c>[Authorize]</c>, and fourteen of them add a role gate.
/// Until now that had only been asserted against isolated controller instances, which never exercises
/// the authentication handler, the role gate, or the endpoint routing. An unauthenticated request
/// that reaches a controller is the failure mode worth locking down, so that is what this class
/// proves.
/// </remarks>
[TestClass]
public sealed class AuthorizationTests
{
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
    public Task ResetDatabaseAsync() => _host.ResetDatabaseAsync();

    [TestMethod]
    [DataRow("/api/profile")]
    [DataRow("/api/auth/me")]
    public async Task Get_WhenNoCredentialIsPresented_ReturnsUnauthorized(string path)
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    [DataRow("/api/admin/dashboard/metrics")]
    [DataRow("/api/admin/dashboard/export")]
    public async Task Get_WhenNoCredentialIsPresented_DoesNotReachAnAdminEndpoint(string path)
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Post_WhenNoCredentialIsPresented_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.PostAsync("/api/auth/logout", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Get_WhenTheBearerTokenIsNotAValidJwt_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Get_WhenTheAccessTokenCookieIsNotAValidJwt_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Add("Cookie", $"{AuthConstants.AccessTokenCookieName}=not-a-jwt");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
