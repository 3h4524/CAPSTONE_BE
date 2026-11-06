using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;

namespace APCS.IntegrationTests.SellerDashboard;

/// <summary>
/// Covers the Seller Dashboard over real HTTP against the real PostgreSQL.
/// </summary>
/// <remarks>
/// The dashboard reads through navigations, correlated subqueries and status lists that a mocked
/// repository evaluates in memory, so only PostgreSQL proves they translate. Every query of both
/// endpoints runs on each call, whether or not rows match, and the ownership filter is checked by
/// reading one Seller's products from a second Seller's account.
/// </remarks>
[TestClass]
public sealed class SellerDashboardEndpointTests
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
    public async Task ResetDatabaseAsync()
    {
        await _host.ResetDatabaseAsync();
        await DedicatedDatabaseReset.TruncateConfiguredAsync();
    }

    [TestMethod]
    public async Task Summary_AfterAddingAProduct_CountsItAsQueuedForItsOwnerOnly()
    {
        _host.EnsureAvailable();
        var owner = await SignInAsync();
        var stranger = await SignInAsync();
        await AddProductAsync(owner.Client);

        var ownSummary = await GetJsonAsync(owner.Client, "/api/dashboard/summary?days=7");
        var strangerSummary = await GetJsonAsync(stranger.Client, "/api/dashboard/summary");

        ownSummary.GetProperty("stage").GetString().Should().Be("producing");
        ownSummary.GetProperty("periodDays").GetInt32().Should().Be(7);
        QueuedCount(ownSummary).Should().Be(1);
        ownSummary.GetProperty("kpis").GetProperty("productsCreated").GetProperty("value").GetDecimal().Should().Be(1);

        strangerSummary.GetProperty("stage").GetString().Should().Be("new");
        QueuedCount(strangerSummary).Should().Be(0);
    }

    [TestMethod]
    public async Task ActionItems_ForASellerWithNothingWaiting_ReturnsAnEmptyList()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        await AddProductAsync(account.Client);

        var items = await GetJsonAsync(account.Client, "/api/dashboard/action-items");

        items.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [TestMethod]
    public async Task DashboardEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = _host.CreateClient();

        using var summary = await client.GetAsync("/api/dashboard/summary");
        using var actionItems = await client.GetAsync("/api/dashboard/action-items");

        summary.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        actionItems.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static int QueuedCount(JsonElement summary) => summary.GetProperty("pipeline").EnumerateArray()
        .Single(stage => stage.GetProperty("stage").GetString() == "queued")
        .GetProperty("count").GetInt32();

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("dashboard") + "@apcs.test");

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AddProductAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("/api/batches", new { name = TestAccount.Unique("Batch"), description = "Fixture batch" });
        created.StatusCode.Should().Be(HttpStatusCode.OK, "the fixture batch must be created");
        var batchId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var product = await client.PostAsJsonAsync(
            $"/api/batches/{batchId}/products",
            new
            {
                name = "Cool Tee",
                productType = "tshirt",
                niche = "Cats",
                keywords = new[] { "cat", "funny" },
                productDescription = "A funny cat tee",
                sourceNotes = "manual",
            });
        product.StatusCode.Should().Be(HttpStatusCode.OK, await product.Content.ReadAsStringAsync());
    }
}
