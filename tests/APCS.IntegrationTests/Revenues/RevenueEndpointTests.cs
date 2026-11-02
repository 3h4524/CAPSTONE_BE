using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Npgsql;

namespace APCS.IntegrationTests.Revenues;

/// <summary>
/// Covers the Admin revenue dashboard's MRR history, MRR Movement, per-plan columns and usage
/// signal over real HTTP against PostgreSQL.
/// </summary>
/// <remarks>
/// The figures are rebuilt from subscriptions, invoices and usage records with queries only a real
/// database runs (timestamp parameters, grouped sums, distinct users through navigations), so these
/// tests seed a plan's customers who stayed and who left and read back what each endpoint reports.
/// The test host charges 25,000 VND per USD.
/// </remarks>
[TestClass]
public sealed class RevenueEndpointTests
{
    private const decimal VndPerUsd = 25_000m;
    private const decimal PriceUsd = 9.99m;
    private const int Stayers = 10;
    private const int Leavers = 10;

    private static IntegrationTestHost _host = null!;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly PeriodStart => Today.AddDays(-29);

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
    public async Task Metrics_ComparesMrrWithTheCustomersWhoWerePayingBeforeAndConvertsToVnd()
    {
        _host.EnsureAvailable();
        await SeedPlanCustomersAsync();

        var body = await GetAsync("metrics");

        var mrr = body.GetProperty("subscriptionMrr");
        mrr.GetProperty("value").GetDecimal().Should().Be(Stayers * PriceUsd * VndPerUsd);
        mrr.GetProperty("previousValue").GetDecimal().Should().Be((Stayers + Leavers) * PriceUsd * VndPerUsd,
            "the customers who left in the period were paying at its start");
        body.GetProperty("activeSubscribers").GetProperty("value").GetDecimal().Should().Be(Stayers);
        body.GetProperty("activeSubscribers").GetProperty("previousValue").GetDecimal().Should().Be(Stayers + Leavers);
    }

    [TestMethod]
    public async Task Charts_ByPlan_ReportsMrrChangeCustomersAndTheUsageSignal()
    {
        _host.EnsureAvailable();
        await SeedPlanCustomersAsync();

        var body = await GetAsync("charts");

        var plan = body.GetProperty("byPlan").EnumerateArray().Single(p => p.GetProperty("planName").GetString() == "Revenue Fixture");
        plan.GetProperty("mrrStart").GetDecimal().Should().Be((Stayers + Leavers) * PriceUsd * VndPerUsd);
        plan.GetProperty("mrrEnd").GetDecimal().Should().Be(Stayers * PriceUsd * VndPerUsd);
        plan.GetProperty("mrrChangePercent").GetDecimal().Should().Be(-50m);
        var customers = plan.GetProperty("customers");
        customers.GetProperty("start").GetInt32().Should().Be(Stayers + Leavers);
        customers.GetProperty("stayed").GetInt32().Should().Be(Stayers);
        customers.GetProperty("stoppedPaying").GetInt32().Should().Be(Leavers);
        customers.GetProperty("end").GetInt32().Should().Be(Stayers);
        plan.GetProperty("retentionRate").GetDecimal().Should().Be(50m);
        var signal = plan.GetProperty("usageSignal");
        signal.GetProperty("hasSignal").GetBoolean().Should().BeTrue();
        signal.GetProperty("featureLabel").GetString().Should().Be("Listing generation");
        signal.GetProperty("stayedRate").GetDecimal().Should().Be(100m);
        signal.GetProperty("leftRate").GetDecimal().Should().Be(0m);
    }

    [TestMethod]
    public async Task Insights_MrrMovement_BridgesThePeriodAndRaisesThePlanAlerts()
    {
        _host.EnsureAvailable();
        await SeedPlanCustomersAsync();

        var body = await GetAsync("insights");

        var all = body.GetProperty("mrrMovement").GetProperty("period").EnumerateArray()
            .Single(p => p.GetProperty("planName").ValueKind == JsonValueKind.Null);
        all.GetProperty("churn").GetDecimal().Should().Be(Leavers * PriceUsd * VndPerUsd);
        all.GetProperty("end").GetDecimal().Should().Be(Stayers * PriceUsd * VndPerUsd);
        body.GetProperty("mrrMovement").GetProperty("months").GetArrayLength().Should().Be(6);

        var alertIds = body.GetProperty("financialAlerts").EnumerateArray().Select(a => a.GetProperty("id").GetString()).ToList();
        alertIds.Should().Contain("plan-mrr-falling-revenue-fixture").And.Contain("plan-usage-gap-revenue-fixture");
    }

    private static async Task<JsonElement> GetAsync(string endpoint)
    {
        using var admin = _host.Factory.CreateAuthenticatedClient(TestAuth.MintToken([TestAuth.AdminRole]));
        using var response = await admin.GetAsync(
            $"/api/admin/revenues/{endpoint}?startDate={PeriodStart:yyyy-MM-dd}&endDate={Today:yyyy-MM-dd}&granularity=Day");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Seeds a plan with customers who paid 40 days ago: the stayers renewed in the period and each
    /// generated a listing; the leavers did neither and were cancelled 10 days ago.
    /// </summary>
    private static async Task SeedPlanCustomersAsync()
    {
        var planId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using (var plan = new NpgsqlCommand(
            """
            INSERT INTO subscription_plans (id, name, tier, description, monthly_price_usd, annual_price_usd, is_active, sort_order)
            VALUES (@id, 'Revenue Fixture', 'revenue-fixture', 'Fixture plan', @price, NULL, true, 1);
            """, connection))
        {
            plan.Parameters.AddWithValue("id", planId);
            plan.Parameters.AddWithValue("price", PriceUsd);
            await plan.ExecuteNonQueryAsync();
        }

        for (var index = 0; index < Stayers + Leavers; index++)
        {
            var stays = index < Stayers;
            var userId = Guid.NewGuid();
            var subscriptionId = Guid.NewGuid();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO users (id, email, full_name, oauth_google_id) VALUES (@userId, @email, 'Revenue fixture', @userId::text);

                INSERT INTO subscriptions
                    (id, user_id, plan_id, billing_cycle, monthly_price_usd, annual_price_usd, status,
                     start_date, renewal_date, auto_renew, cancelled_at, created_at)
                VALUES
                    (@id, @userId, @planId, 'monthly', @price, NULL, @status,
                     @firstPaid, @renewalDate, true, @cancelledAt, @firstPaid);

                INSERT INTO invoices (id, subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd, tax_amount, total_amount, status, payment_date)
                VALUES (gen_random_uuid(), @id, @userId, @number || '-1', @firstPaid, @firstPaid, @price, 0, @price, 'paid', @firstPaid);
                """, connection);
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("email", $"revenue-{index}-{Guid.NewGuid():N}@apcs.test");
            command.Parameters.AddWithValue("id", subscriptionId);
            command.Parameters.AddWithValue("planId", planId);
            command.Parameters.AddWithValue("price", PriceUsd);
            command.Parameters.AddWithValue("status", stays ? "active" : "cancelled");
            command.Parameters.AddWithValue("firstPaid", Today.AddDays(-40));
            command.Parameters.AddWithValue("renewalDate", stays ? Today.AddDays(20) : Today.AddDays(-10));
            command.Parameters.AddWithValue("cancelledAt", stays ? DBNull.Value : DateTime.UtcNow.AddDays(-10));
            command.Parameters.AddWithValue("number", $"INV-REV-{index}-{subscriptionId:N}"[..30]);
            await command.ExecuteNonQueryAsync();

            if (stays)
            {
                await using var usage = new NpgsqlCommand(
                    """
                    INSERT INTO invoices (id, subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd, tax_amount, total_amount, status, payment_date)
                    VALUES (gen_random_uuid(), @id, @userId, @number || '-2', @renewed, @renewed, @price, 0, @price, 'paid', @renewed);

                    INSERT INTO api_usage_records (id, user_id, provider, feature, request_units, cost_usd, status, created_at)
                    VALUES (gen_random_uuid(), @userId, 'gemini', 'listing', 1, 0.002, 'success', now() - interval '5 days');
                    """, connection);
                usage.Parameters.AddWithValue("id", subscriptionId);
                usage.Parameters.AddWithValue("userId", userId);
                usage.Parameters.AddWithValue("number", $"INV-REV-{index}-{subscriptionId:N}"[..30]);
                usage.Parameters.AddWithValue("renewed", Today.AddDays(-10));
                usage.Parameters.AddWithValue("price", PriceUsd);
                await usage.ExecuteNonQueryAsync();
            }
        }
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
