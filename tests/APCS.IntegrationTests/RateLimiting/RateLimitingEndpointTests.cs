using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace APCS.IntegrationTests.RateLimiting;

[TestClass]
public sealed class RateLimitingEndpointTests
{
    private const string LoginRoute = "/api/auth/login";
    private const string MeRoute = "/api/auth/me";

    private static IntegrationTestHost _host = null!;
    private static WebApplicationFactory<Program> _authLimited = null!;
    private static WebApplicationFactory<Program> _anonymousLimited = null!;

    [ClassInitialize]
    public static async Task StartHostAsync(TestContext testContext)
    {
        _host = await IntegrationTestHost.StartAsync();
        if (!_host.IsAvailable)
            return;

        _authLimited = WithLimits(authPermits: "2", anonymousPermits: "1000");
        _anonymousLimited = WithLimits(authPermits: "1000", anonymousPermits: "3");
    }

    private static WebApplicationFactory<Program> WithLimits(string authPermits, string anonymousPermits) =>
        _host.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:AuthPermitsPerMinute"] = authPermits,
                ["RateLimiting:AnonymousPermitsPerMinute"] = anonymousPermits
            })));

    [ClassCleanup]
    public static async Task StopHostAsync()
    {
        if (_authLimited is not null)
            await _authLimited.DisposeAsync();
        if (_anonymousLimited is not null)
            await _anonymousLimited.DisposeAsync();
        await _host.DisposeAsync();
    }

    [TestMethod]
    public async Task Login_WhenTheAuthLimitIsExceeded_ReturnsTooManyRequestsWithRetryAfter()
    {
        _host.EnsureAvailable();
        using var client = _authLimited.CreateClient();
        var credentials = new { email = "nobody@apcs.test", password = "Wrong-password-1" };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await client.PostAsJsonAsync(LoginRoute, credentials);
            allowed.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        using var response = await client.PostAsJsonAsync(LoginRoute, credentials);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("system.too_many_requests");
        body.GetProperty("title").GetString().Should().StartWith("Too many requests.");
    }

    [TestMethod]
    public async Task AnonymousRequests_WhenTheGlobalLimitIsExceeded_ReturnTooManyRequests()
    {
        _host.EnsureAvailable();
        using var client = _anonymousLimited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var response = await client.GetAsync(MeRoute);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Health_WhenCalledBeyondTheAnonymousLimit_IsNeverLimited()
    {
        _host.EnsureAvailable();
        using var client = _anonymousLimited.CreateClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await client.GetAsync("/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [TestMethod]
    [DataRow("/internal/media-jobs/claim")]
    [DataRow("/api/subscriptions/webhooks/payos")]
    [DataRow("/hubs/notification/negotiate?negotiateVersion=1")]
    public async Task OptedOutEndpoint_WhenCalledBeyondTheAnonymousLimit_IsNeverLimited(string path)
    {
        _host.EnsureAvailable();
        using var client = _anonymousLimited.CreateClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await client.PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [TestMethod]
    public async Task Login_WhenRateLimitingIsDisabled_IsNeverLimited()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();
        var credentials = new { email = "nobody@apcs.test", password = "Wrong-password-1" };

        for (var attempt = 0; attempt < 25; attempt++)
        {
            using var response = await client.PostAsJsonAsync(LoginRoute, credentials);
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }
}
