using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace APCS.IntegrationTests.Auth;

/// <summary>
/// Covers the public login endpoint over real HTTP, proving that a rejected sign-in is answered
/// from the application's own error mapping and never issues a session.
/// </summary>
[TestClass]
public sealed class AuthEndpointTests
{
    private const string UnknownEmail = "nobody@apcs.invalid";

    private const string WrongPassword = "DefinitelyNotThePassword1!";

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
    public async Task Login_WhenThePasswordIsWrong_ReturnsUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = UnknownEmail, password = WrongPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadProblemCodeAsync(response)).Should().Be(ErrorCodes.InvalidCredentials);
    }

    [TestMethod]
    public async Task Login_WhenTheEmailIsMalformed_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "not-an-email", password = WrongPassword });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemCodeAsync(response)).Should().Be(ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task Login_WhenTheCredentialsAreRejected_IssuesNoSessionCookie()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = UnknownEmail, password = WrongPassword });

        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [TestMethod]
    public async Task Login_WhenTheRequestBodyIsMissing_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.PostAsync(
            "/api/auth/login",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)response.StatusCode);
        problem.Extensions.Should().ContainKey("code");

        var code = problem.Extensions["code"];

        code.Should().BeOfType<JsonElement>();

        return ((JsonElement)code!).GetString()!;
    }
}
