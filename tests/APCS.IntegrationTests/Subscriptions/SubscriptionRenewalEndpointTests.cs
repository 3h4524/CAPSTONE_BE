using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Common.Constants;
using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace APCS.IntegrationTests.Subscriptions;

/// <summary>
/// Covers renewing a paid plan and what happens to one left unpaid, over real HTTP against
/// PostgreSQL.
/// </summary>
/// <remarks>
/// The grace period lives in the query that finds the Seller's plan and in the hourly job's bulk
/// updates, both of which only a real database runs, so these tests seed a paid subscription at
/// a chosen renewal date and read back what the endpoints and the job make of it.
/// </remarks>
[TestClass]
public sealed class SubscriptionRenewalEndpointTests
{
    private const string OverviewRoute = "/api/subscriptions/overview";
    private const string RenewRoute = "/api/subscriptions/renew";
    private const string EntitlementsRoute = "/api/subscriptions/entitlements";

    private static IntegrationTestHost _host = null!;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

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
    public async Task Overview_PastTheRenewalDate_ShowsPastDueAndKeepsThePlanThroughGrace()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        await SeedPaidSubscriptionAsync(account.UserId, renewalDate: Today.AddDays(-1));

        using var overview = await account.Client.GetAsync(OverviewRoute);
        using var entitlements = await account.Client.GetAsync(EntitlementsRoute);

        overview.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await overview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("currentSubscription");
        current.GetProperty("status").GetString().Should().Be("past_due");
        current.GetProperty("canRenew").GetBoolean().Should().BeTrue();
        current.GetProperty("lastGraceDay").GetString().Should().Be(Today.AddDays(1).ToString("yyyy-MM-dd"));
        (await entitlements.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasPlan").GetBoolean()
            .Should().BeTrue("the plan keeps working during the grace period");
    }

    [TestMethod]
    public async Task Overview_AfterTheGracePeriod_IsBackOnFree()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        await SeedPaidSubscriptionAsync(account.UserId, renewalDate: Today.AddDays(-3));

        using var overview = await account.Client.GetAsync(OverviewRoute);
        using var entitlements = await account.Client.GetAsync(EntitlementsRoute);

        var body = await overview.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("currentSubscription").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("hasActivePaidPlan").GetBoolean().Should().BeFalse();
        (await entitlements.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasPlan").GetBoolean()
            .Should().BeFalse("access ends with the grace period even before the job marks it expired");
    }

    [TestMethod]
    public async Task Renew_WhenPastDue_BillsTheSameSubscription()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var subscriptionId = await SeedPaidSubscriptionAsync(account.UserId, renewalDate: Today);

        using var response = await account.Client.PostAsync(RenewRoute, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var invoiceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invoiceId").GetGuid();
        var (invoiceSubscriptionId, status, items) = await ReadInvoiceAsync(invoiceId);
        invoiceSubscriptionId.Should().Be(subscriptionId);
        status.Should().Be("issued");
        items.Should().Contain("\"renewal\": true");
    }

    [TestMethod]
    public async Task Renew_EarlyInTheCycle_IsRefused()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        await SeedPaidSubscriptionAsync(account.UserId, renewalDate: Today.AddDays(20));

        using var response = await account.Client.PostAsync(RenewRoute, content: null);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.SubscriptionRenewalNotDue);
    }

    [TestMethod]
    public async Task Renew_WithoutAPaidPlan_IsRefused()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.PostAsync(RenewRoute, content: null);

        await response.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.SubscriptionNothingToRenew);
    }

    [TestMethod]
    public async Task RenewalJob_MarksUnpaidPlansPastDueThenExpired()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();
        var other = await SignInAsync();
        var third = await SignInAsync();
        var paid = await SeedPaidSubscriptionAsync(account.UserId, renewalDate: Today.AddDays(10));
        var pastDue = await SeedPaidSubscriptionAsync(other.UserId, renewalDate: Today.AddDays(-1));
        var lapsed = await SeedPaidSubscriptionAsync(third.UserId, renewalDate: Today.AddDays(-5));

        using (var scope = _host.Factory.Services.CreateScope())
        {
            var result = await SubscriptionRenewalJob.RunAsync(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(), DateTime.UtcNow, CancellationToken.None);
            result.Should().Be((1, 1));
        }

        (await ReadStatusAsync(paid)).Status.Should().Be("active");
        (await ReadStatusAsync(pastDue)).Status.Should().Be("past_due");
        var expired = await ReadStatusAsync(lapsed);
        expired.Status.Should().Be("expired");
        DateOnly.FromDateTime(expired.CancelledAt!.Value).Should().Be(Today.AddDays(-5), "churn is dated to the unpaid renewal");
    }

    private static Task<AuthenticatedAccount> SignInAsync() =>
        TestAccount.SignInAsync(_host, TestAccount.Unique("renew") + "@apcs.test");

    private static async Task<Guid> SeedPaidSubscriptionAsync(Guid userId, DateOnly renewalDate)
    {
        var planId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO subscription_plans
                (id, name, tier, description, monthly_price_usd, annual_price_usd, is_active, sort_order)
            VALUES
                (@planId, @name, @tier, 'Fixture plan', 9.99, 99.99, true, 1);

            INSERT INTO subscriptions
                (id, user_id, plan_id, billing_cycle, monthly_price_usd, annual_price_usd, status,
                 start_date, renewal_date, auto_renew, created_at)
            VALUES
                (@id, @userId, @planId, 'monthly', 9.99, 99.99, 'active',
                 @startDate, @renewalDate, true, now());
            """,
            connection);
        command.Parameters.AddWithValue("planId", planId);
        command.Parameters.AddWithValue("name", "Renewal plan " + planId.ToString("N")[..6]);
        command.Parameters.AddWithValue("tier", "renew-" + planId.ToString("N")[..6]);
        command.Parameters.AddWithValue("id", subscriptionId);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("startDate", renewalDate.AddMonths(-1));
        command.Parameters.AddWithValue("renewalDate", renewalDate);
        await command.ExecuteNonQueryAsync();

        return subscriptionId;
    }

    private static async Task<(Guid SubscriptionId, string Status, string Items)> ReadInvoiceAsync(Guid invoiceId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT subscription_id, status, items::text FROM invoices WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<(string Status, DateTime? CancelledAt)> ReadStatusAsync(Guid subscriptionId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status, cancelled_at FROM subscriptions WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", subscriptionId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDateTime(1));
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
