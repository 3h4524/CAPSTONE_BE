using System.Net;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Infrastructure.BackgroundServices;
using APCS.IntegrationTests.TestSupport;
using APCS.IntegrationTests.TestSupport.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace APCS.IntegrationTests.Health;

/// <summary>
/// Proves the API host boots end to end: the whole DI graph, the middleware pipeline, and routing
/// resolve against a real PostgreSQL and Redis.
/// </summary>
[TestClass]
public sealed class HealthEndpointTests
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
    public async Task Get_WhenTheHostIsBooted_ReturnsHealthy()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [TestMethod]
    public async Task Get_WhenTheHostIsBooted_DoesNotRedirectToHttps()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Location.Should().BeNull();
    }

    [TestMethod]
    public void Host_WhenItIsBooted_ResolvesNoRealThirdPartyAdapter()
    {
        _host.EnsureAvailable();

        var services = _host.Factory.Services;

        services.GetRequiredService<IEmailService>().Should().BeOfType<FakeEmailService>();
        services.GetRequiredService<IPaymentGatewayClient>().Should().BeOfType<FakePaymentGatewayClient>();
        services.GetRequiredService<IFileStorageService>().Should().BeOfType<FakeFileStorageService>();
        services.GetRequiredService<IPublicImageService>().Should().BeOfType<FakePublicImageService>();
        services.GetRequiredService<IGoogleAuthService>().Should().BeOfType<FakeGoogleAuthService>();
    }

    [TestMethod]
    public async Task Host_WhenItIsBooted_BlocksEveryOutboundHttpRequest()
    {
        _host.EnsureAvailable();

        var services = _host.Factory.Services;
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("ApiKeyValidation");

        var act = async () => await client.GetAsync("https://example.invalid/probe");

        await act.Should().ThrowAsync<OutboundNetworkBlockedException>();
    }

    [TestMethod]
    public void Host_WhenItIsBooted_DoesNotRunTheUnbanUsersJob()
    {
        _host.EnsureAvailable();

        var services = _host.Factory.Services;

        services.GetServices<IHostedService>()
            .Should().NotContain(service => service is UnbanUsersJob);
    }
}
