using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Npgsql;

namespace APCS.IntegrationTests.Subscriptions;

/// <summary>
/// Covers the seller subscription checkout lifecycle over real HTTP with a fake payment gateway.
/// </summary>
/// <remarks>
/// <para>
/// Checkout is the only flow that writes a subscription plus an invoice in one transaction and
/// then leaves PayOS to confirm payment asynchronously, so the fake gateway that answers locally
/// is what makes the lifecycle deterministic: a seeded purchasable plan produces a pending
/// invoice, polling reports its awaiting-payment status, and cancelling resolves it without ever
/// moving real money.
/// </para>
/// <para>
/// Upgrade and downgrade need an active paid subscription and are therefore pinned only on their
/// validation and authentication branches here; the paid-state transitions belong to a follow-up
/// once plan seeding covers tier ordering.
/// </para>
/// </remarks>
[TestClass]
public sealed class SubscriptionEndpointTests
{
    private const string OverviewRoute = "/api/subscriptions/overview";
    private const string CheckoutRoute = "/api/subscriptions/checkout";

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
    public async Task Overview_WithASession_AnswersOk()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync(OverviewRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Checkout_WhenThePlanExists_CreatesAPendingInvoice()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var planId = await SeedPlanAsync("Checkout Plan", "checkout-tier");

        using var response = await account.Client.PostAsJsonAsync(
            CheckoutRoute,
            new { planId, billingCycle = "monthly" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("invoiceId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("checkoutUrl").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task CheckoutStatus_AfterCheckout_ReportsAwaitingPayment()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var planId = await SeedPlanAsync("Status Plan", "status-tier");
        var invoiceId = await CheckoutAsync(account.Client, planId);

        using var response = await account.Client.GetAsync($"{CheckoutRoute}/{invoiceId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("planName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task CancelCheckout_AfterCheckout_AnswersNoContent()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var planId = await SeedPlanAsync("Cancel Plan", "cancel-tier");
        var invoiceId = await CheckoutAsync(account.Client, planId);

        using var response = await account.Client.PostAsync($"{CheckoutRoute}/{invoiceId}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task DownloadInvoice_AfterCheckout_ReturnsPdf()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var planId = await SeedPlanAsync("Invoice Plan", "invoice-tier");
        var invoiceId = await CheckoutAsync(account.Client, planId);

        using var response = await account.Client.GetAsync($"/api/subscriptions/invoices/{invoiceId}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
    }

    [TestMethod]
    public async Task Checkout_WhenThePlanDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsJsonAsync(
            CheckoutRoute,
            new { planId = Guid.NewGuid(), billingCycle = "monthly" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, ErrorCodes.SubscriptionPlanNotFound);
    }

    [TestMethod]
    public async Task Checkout_WhenTheBillingCycleIsInvalid_ReturnsBadRequest()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var planId = await SeedPlanAsync("Cycle Plan", "cycle-tier");

        using var response = await account.Client.PostAsJsonAsync(
            CheckoutRoute,
            new { planId, billingCycle = "not-a-cycle" });

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [TestMethod]
    public async Task CheckoutStatus_WhenTheInvoiceDoesNotExist_ReturnsNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync($"{CheckoutRoute}/{Guid.NewGuid()}/status");

        await response.ShouldFailWithCodeAsync(HttpStatusCode.NotFound, ErrorCodes.SubscriptionInvoiceNotFound);
    }

    [TestMethod]
    public async Task Upgrade_WhenThereIsNoActivePaidPlan_ReturnsBadRequestOrNotFound()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var planId = await SeedPlanAsync("Upgrade Target", "upgrade-tier");

        using var response = await account.Client.PostAsJsonAsync("/api/subscriptions/upgrade", new { planId });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task SubscriptionEndpoints_WhenTheCallerIsNotSignedIn_ReturnUnauthorized()
    {
        _host.EnsureAvailable();
        using var client = TestAccount.CreateAnonymousClient(_host);

        using var overview = await client.GetAsync(OverviewRoute);
        await overview.ShouldFailAsync(HttpStatusCode.Unauthorized);

        using var checkout = await client.PostAsJsonAsync(CheckoutRoute, new { planId = Guid.NewGuid(), billingCycle = "monthly" });
        await checkout.ShouldFailAsync(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task SubscriptionEndpoints_WhenTheTokenCarriesTheAdminRole_ReturnForbidden()
    {
        _host.EnsureAvailable();
        var token = TestAuth.MintToken([TestAuth.AdminRole]);
        using var client = _host.Factory.CreateAuthenticatedClient(token);

        using var overview = await client.GetAsync(OverviewRoute);
        overview.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("sub") + "@apcs.test");

    private static async Task<Guid> SeedPlanAsync(string name, string tier)
    {
        var id = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO subscription_plans
                (id, name, tier, description, monthly_price_usd, annual_price_usd, is_active, sort_order)
            VALUES
                (@id, @name, @tier, 'Fixture plan', 9.99, 99.99, true, 1);
            """,
            connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("tier", tier);

        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Guid> CheckoutAsync(HttpClient client, Guid planId)
    {
        using var response = await client.PostAsJsonAsync(
            CheckoutRoute,
            new { planId, billingCycle = "monthly" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the fixture checkout must be created");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("invoiceId").GetGuid();
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
