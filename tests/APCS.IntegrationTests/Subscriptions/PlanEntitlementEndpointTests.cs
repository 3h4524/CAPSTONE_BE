using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Common.Constants;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Npgsql;

namespace APCS.IntegrationTests.Subscriptions;

/// <summary>
/// Covers the free Pro trial, the Seller's entitlements and the Admin's Plan Insights over real
/// HTTP against PostgreSQL.
/// </summary>
/// <remarks>
/// Plan Insights aggregates across many tables with projections and subqueries that only a real
/// database translates, so these tests pin that the queries run as well as what they return.
/// </remarks>
[TestClass]
public sealed class PlanEntitlementEndpointTests
{
    private const string TrialRoute = "/api/subscriptions/trial";
    private const string EntitlementsRoute = "/api/subscriptions/entitlements";
    private const string InsightsRoute = "/api/admin/subscription-plans/insights";
    private const string AllowancesRoute = "/api/allowances";

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
    public async Task Entitlements_OnFree_RunOneProductWithListingsButNoProTools()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync(EntitlementsRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("hasPlan").GetBoolean().Should().BeFalse();
        body.GetProperty("maxBatchSize").GetInt32().Should().Be(1);
        Feature(body, "ai_video").GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        Feature(body, "workflow_listing").GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        Feature(body, "design_variations").GetProperty("limitValue").GetInt32().Should().Be(1);
    }

    [TestMethod]
    public async Task Allowances_OnFree_CountThisMonthFromRealRecords()
    {
        _host.EnsureAvailable();
        var account = await SignInAsync();

        using var response = await account.Client.GetAsync(AllowancesRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("scope").GetString().Should().Be("free");
        body.GetProperty("mockupsPerRun").GetInt32().Should().Be(3);
        body.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("code").GetString(), item.GetProperty("used").GetInt32(), item.GetProperty("limit").GetInt32()))
            .Should().Equal(("full_runs", 0, 2), ("design_runs", 0, 5), ("design_images", 0, 7), ("mockups", 0, 6), ("videos", 0, 2), ("listings", 0, 5));
    }

    [TestMethod]
    public async Task Trial_WithoutAGeminiKey_IsRefused()
    {
        _host.EnsureAvailable();
        await SeedProPlanAsync();
        var account = await SignInAsync(withGeminiKey: false);

        using var trial = await account.Client.PostAsync(TrialRoute, content: null);

        await trial.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, "Plan.GeminiKeyRequired");
    }

    [TestMethod]
    public async Task Trial_ForANewSeller_UnlocksProButNotTheAiVideoTools()
    {
        _host.EnsureAvailable();
        await SeedProPlanAsync();
        var account = await SignInAsync();

        using var trial = await account.Client.PostAsync(TrialRoute, content: null);

        trial.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await trial.Content.ReadFromJsonAsync<JsonElement>();
        started.GetProperty("planName").GetString().Should().Be("Pro Fixture");

        using var entitlements = await account.Client.GetAsync(EntitlementsRoute);
        var body = await entitlements.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("hasPlan").GetBoolean().Should().BeTrue();
        body.GetProperty("isTrial").GetBoolean().Should().BeTrue();
        body.GetProperty("maxBatchSize").GetInt32().Should().Be(3);
        Feature(body, "ai_video").GetProperty("isEnabled").GetBoolean().Should().BeFalse("the trial has standard videos only");

        using var allowances = await account.Client.GetAsync(AllowancesRoute);
        var used = await allowances.Content.ReadFromJsonAsync<JsonElement>();
        used.GetProperty("scope").GetString().Should().Be("trial");
        used.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("code").GetString())
            .Should().Equal("products", "design_images", "mockups", "videos", "listings");
    }

    [TestMethod]
    public async Task Trial_ASecondTime_IsRefused()
    {
        _host.EnsureAvailable();
        await SeedProPlanAsync();
        var account = await SignInAsync();
        using var first = await account.Client.PostAsync(TrialRoute, content: null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await account.Client.PostAsync(TrialRoute, content: null);

        await second.ShouldFailWithCodeAsync(HttpStatusCode.BadRequest, ErrorCodes.SubscriptionTrialNotAvailableWithPlan);
    }

    [TestMethod]
    public async Task Overview_DuringATrial_ReportsItAndNoPaidPlan()
    {
        _host.EnsureAvailable();
        await SeedProPlanAsync();
        var account = await SignInAsync();
        using var trial = await account.Client.PostAsync(TrialRoute, content: null);

        using var response = await account.Client.GetAsync("/api/subscriptions/overview");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("currentSubscription").GetProperty("isTrial").GetBoolean().Should().BeTrue();
        body.GetProperty("hasActivePaidPlan").GetBoolean().Should().BeFalse();
        body.GetProperty("trialEligible").GetBoolean().Should().BeFalse();
    }

    [TestMethod]
    public async Task Insights_WithAnAdminSession_AggregatesPlansAndTrials()
    {
        _host.EnsureAvailable();
        await SeedProPlanAsync();
        var seller = await SignInAsync();
        using var trial = await seller.Client.PostAsync(TrialRoute, content: null);
        trial.StatusCode.Should().Be(HttpStatusCode.OK);
        using var admin = _host.Factory.CreateAuthenticatedClient(TestAuth.MintToken([TestAuth.AdminRole]));

        using var response = await admin.GetAsync(InsightsRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("days").GetInt32().Should().Be(45, "Plan Insights and the AI review cover the same 45 days");
        body.GetProperty("trial").GetProperty("started").GetInt32().Should().Be(1);
        body.GetProperty("trial").GetProperty("running").GetInt32().Should().Be(1);
        // Trials are compared on every Pro feature, the Pro-only ones included.
        body.GetProperty("trial").GetProperty("featureComparison").EnumerateArray()
            .Select(feature => feature.GetProperty("metric").GetString())
            .Should().Contain(["ai_videos", "design_variations", "auto_approval", "custom_mockup_templates", "listing_regenerations", "landscape_videos"]);
        // The only trial is still running, so none has finished and there is no rate yet rather than 0%.
        body.GetProperty("trial").GetProperty("conversionRate").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("plans").EnumerateArray().Should().Contain(plan => plan.GetProperty("isTrial").GetBoolean());
        // The trial has allowances for its whole length instead of a daily cap; nobody has made a mockup yet.
        var mockups = body.GetProperty("plans").EnumerateArray().Single(plan => plan.GetProperty("isTrial").GetBoolean())
            .GetProperty("adoption").EnumerateArray().Single(feature => feature.GetProperty("metric").GetString() == "mockups");
        mockups.GetProperty("dailyCap").ValueKind.Should().Be(JsonValueKind.Null);
        mockups.GetProperty("usersAtDailyCap").ValueKind.Should().Be(JsonValueKind.Null);
        mockups.GetProperty("medianUses").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("pipeline").GetArrayLength().Should().Be(11);
        // Unit costs are measured in USD and sent in VND at the PayOS rate: $0.002 and $0.01 at 25,000.
        var unitCosts = body.GetProperty("unitCosts").EnumerateArray()
            .ToDictionary(cost => cost.GetProperty("key").GetString()!, cost => cost.GetProperty("costVnd").GetDecimal());
        unitCosts["mockup_render"].Should().Be(50m);
        unitCosts["video_render"].Should().Be(250m);
        // Only paid plans are judged; the one Seller here is on a trial.
        body.GetProperty("verdicts").GetArrayLength().Should().Be(0);
        body.GetProperty("churnSignals").GetArrayLength().Should().Be(0);
        body.GetProperty("verdictRules").GetProperty("lowRetentionRate").GetDecimal().Should().Be(60m, "the appsettings default");
    }

    [TestMethod]
    public async Task AiReview_BeforeTheFirstReview_ReturnsNoReviewAndNoPreviousScores()
    {
        _host.EnsureAvailable();
        using var admin = _host.Factory.CreateAuthenticatedClient(TestAuth.MintToken([TestAuth.AdminRole]));

        using var response = await admin.GetAsync("/api/admin/subscription-plans/ai-review");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("latest").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("previousScores").GetArrayLength().Should().Be(0);
        body.GetProperty("isGenerating").GetBoolean().Should().BeFalse();
    }

    [TestMethod]
    public async Task AdminUpdate_ChangingAFeatureThePlanAlreadyHas_SavesAndLocksItForTheSeller()
    {
        _host.EnsureAvailable();
        var planId = await SeedProPlanAsync();
        var seller = await SignInAsync();
        using var trial = await seller.Client.PostAsync(TrialRoute, content: null);
        trial.StatusCode.Should().Be(HttpStatusCode.OK);
        // A real account: saving the change logs who made it, and the log refers to the users table.
        var adminAccount = await SignInAsync(withGeminiKey: false);
        using var admin = _host.Factory.CreateAuthenticatedClient(TestAuth.MintToken([TestAuth.AdminRole], adminAccount.UserId));
        var plan = JsonNode.Parse(await admin.GetStringAsync($"/api/admin/subscription-plans/{planId}"))!.AsObject();
        // Rows the plan already has, so the update writes over existing plan_features keys.
        plan["features"] = new JsonArray(
            new JsonObject { ["code"] = "ai_video", ["isEnabled"] = false, ["limitValue"] = null },
            new JsonObject { ["code"] = "design_variations", ["isEnabled"] = true, ["limitValue"] = 2 });

        using var update = await admin.PutAsJsonAsync($"/api/admin/subscription-plans/{planId}", plan);

        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        using var entitlements = await seller.Client.GetAsync(EntitlementsRoute);
        var body = await entitlements.Content.ReadFromJsonAsync<JsonElement>();
        Feature(body, "ai_video").GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        Feature(body, "design_variations").GetProperty("limitValue").GetInt32().Should().Be(2);
    }

    [TestMethod]
    public async Task Insights_WhenTheTokenCarriesTheSellerRole_ReturnsForbidden()
    {
        _host.EnsureAvailable();
        using var client = _host.Factory.CreateAuthenticatedClient(TestAuth.MintToken([TestAuth.SellerRole]));

        using var response = await client.GetAsync(InsightsRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static JsonElement Feature(JsonElement entitlements, string code) =>
        entitlements.GetProperty("features").EnumerateArray().Single(feature => feature.GetProperty("code").GetString() == code);

    // The free Pro trial needs a working Gemini key, so every account gets one unless a test says otherwise.
    private static async Task<AuthenticatedAccount> SignInAsync(bool withGeminiKey = true)
    {
        var account = await TestAccount.SignInAsync(_host, TestAccount.Unique("plan") + "@apcs.test");
        if (withGeminiKey) await SeedGeminiKeyAsync(account.UserId);
        return account;
    }

    private static async Task SeedGeminiKeyAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO api_keys
                (id, user_id, service_provider, key_identifier, key_value_encrypted, key_last_4,
                 is_active, auth_type, environment, created_at, last_checked_at, last_check_succeeded)
            VALUES
                (gen_random_uuid(), @userId, 'gemini', 'Gemini', 'dummy-encrypted', '1234',
                 true, 'api_key', 'Production', now(), now(), true);
            """,
            connection);
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedProPlanAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO subscription_plans
                (id, name, tier, description, monthly_price_usd, annual_price_usd, max_batch_size,
                 max_products_per_month, max_concurrent_jobs, image_generation_quota, video_generation_quota,
                 api_call_quota, storage_quota_gb, is_active, sort_order)
            VALUES
                (@id, 'Pro Fixture', 'pro', 'Fixture plan', 29.99, 299.99, 200, 0, 5, 0, 0, 0, 0, true, 2);
            INSERT INTO plan_features (plan_id, feature_code, is_enabled, limit_value)
            VALUES (@id, 'ai_video', true, NULL), (@id, 'design_variations', true, 4), (@id, 'daily_mockup_cap', true, 1500);
            """,
            connection);
        command.Parameters.AddWithValue("id", id);

        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
