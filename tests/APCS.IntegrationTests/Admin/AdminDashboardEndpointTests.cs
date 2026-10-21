using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.Admin;

/// <summary>
/// Covers the admin dashboard read endpoints over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard has no write verbs by design: metrics aggregate users, subscriptions, invoices,
/// jobs, and tickets that other flows own, and export renders the same aggregation as CSV. The
/// guarantees worth pinning are therefore that an administrator reaches both routes, a seller is
/// stopped by the role gate before any aggregation runs, and an anonymous caller is stopped by
/// authentication.
/// </para>
/// </remarks>
[TestClass]
public sealed class AdminDashboardEndpointTests
{
    private const string MetricsRoute = "/api/admin/dashboard/metrics";
    private const string ExportRoute = "/api/admin/dashboard/export";

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
    public async Task Metrics_WithAnAdminSession_AnswersOk()
    {
        _host.EnsureAvailable();
        using var client = AdminClient();

        using var response = await client.GetAsync(MetricsRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [TestMethod]
    public async Task Metrics_WithDayRange_AnswersOk()
    {
        _host.EnsureAvailable();
        using var client = AdminClient();

        using var response = await client.GetAsync($"{MetricsRoute}?timeRange=day");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Export_WithAnAdminSession_ReturnsCsv()
    {
        _host.EnsureAvailable();
        using var client = AdminClient();

        using var response = await client.GetAsync(ExportRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
    }

    [TestMethod]
    public async Task DashboardEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var metrics = await client.GetAsync(MetricsRoute);
        await metrics.ShouldFailAsync(HttpStatusCode.Unauthorized);

        using var export = await client.GetAsync(ExportRoute);
        await export.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task DashboardEndpoints_WhenTheTokenCarriesTheSellerRole_ReturnForbidden()
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([TestAuth.SellerRole]);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var metrics = await client.GetAsync(MetricsRoute);
        metrics.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var export = await client.GetAsync(ExportRoute);
        export.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static HttpClient AdminClient()
    {
        var token = TestAuth.MintToken([TestAuth.AdminRole]);
        return _host.Factory.CreateAuthenticatedClient(token);
    }
}
