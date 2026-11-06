using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace APCS.IntegrationTests.Subscriptions;

/// <summary>
/// Covers the hourly subscription jobs against PostgreSQL: their bulk updates and deletes need a
/// real database, and they must leave alone every row they do not own.
/// </summary>
[TestClass]
public sealed class SubscriptionJobsTests
{
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
    public async Task RenewalJob_OnTheRenewalDate_MarksThePlanPastDue()
    {
        _host.EnsureAvailable();
        var subscription = await SeedSubscriptionAsync(await SeedPlanAsync(), renewalDate: Today);

        (await RunRenewalPassAsync()).Should().Be((1, 0));

        (await ReadStatusAsync(subscription)).Should().Be("past_due");
    }

    [TestMethod]
    public async Task RenewalJob_OnTheLastGraceDay_KeepsThePlanPastDue()
    {
        _host.EnsureAvailable();
        var subscription = await SeedSubscriptionAsync(await SeedPlanAsync(), renewalDate: Today.AddDays(-2), status: "past_due");

        (await RunRenewalPassAsync()).Should().Be((0, 0));

        (await ReadStatusAsync(subscription)).Should().Be("past_due");
    }

    [TestMethod]
    public async Task RenewalJob_TheDayAfterGraceEnds_ExpiresThePlan()
    {
        _host.EnsureAvailable();
        var subscription = await SeedSubscriptionAsync(await SeedPlanAsync(), renewalDate: Today.AddDays(-3), status: "past_due");

        (await RunRenewalPassAsync()).Should().Be((0, 1));

        (await ReadStatusAsync(subscription)).Should().Be("expired");
    }

    [TestMethod]
    public async Task RenewalJob_LeavesAloneWhatIsNotAnUnpaidPlan()
    {
        _host.EnsureAvailable();
        var plan = await SeedPlanAsync();
        var overdue = Today.AddDays(-30);
        var trial = await SeedSubscriptionAsync(plan, overdue, trialEndsAt: DateTime.UtcNow.AddDays(3));
        var deleted = await SeedSubscriptionAsync(plan, overdue, deletedAt: DateTime.UtcNow);
        var cancelled = await SeedSubscriptionAsync(plan, overdue, status: "cancelled");

        (await RunRenewalPassAsync()).Should().Be((0, 0));

        (await ReadStatusAsync(trial)).Should().Be("active");
        (await ReadStatusAsync(deleted)).Should().Be("active");
        (await ReadStatusAsync(cancelled)).Should().Be("cancelled");
    }

    [TestMethod]
    public async Task RenewalJob_RunTwice_ChangesNothingTheSecondTime()
    {
        _host.EnsureAvailable();
        var plan = await SeedPlanAsync();
        await SeedSubscriptionAsync(plan, Today);
        await SeedSubscriptionAsync(plan, Today.AddDays(-10));

        (await RunRenewalPassAsync()).Should().Be((1, 1));
        (await RunRenewalPassAsync()).Should().Be((0, 0));
    }

    [TestMethod]
    public async Task SpentGrants_RemovesOnlyThoseThatNoLongerCount()
    {
        _host.EnsureAvailable();
        var plan = await SeedPlanAsync();
        var otherPlan = await SeedPlanAsync();
        var future = Today.AddDays(10);
        var live = await SeedSubscriptionAsync(plan, future);
        var pastDue = await SeedSubscriptionAsync(plan, Today.AddDays(-1), status: "past_due");
        var cancelled = await SeedSubscriptionAsync(plan, future, status: "cancelled");
        var deleted = await SeedSubscriptionAsync(plan, future, deletedAt: DateTime.UtcNow);
        var moved = await SeedSubscriptionAsync(otherPlan, future);

        var kept = await SeedGrantAsync(live, plan, future);
        var keptPastDue = await SeedGrantAsync(pastDue, plan, future);
        await SeedGrantAsync(live, plan, Today, "ai_video");
        await SeedGrantAsync(cancelled, plan, future);
        await SeedGrantAsync(deleted, plan, future);
        await SeedGrantAsync(moved, plan, future);

        using var scope = _host.Factory.Services.CreateScope();
        var removed = await SubscriptionRenewalJob.RemoveSpentGrantsAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), DateTime.UtcNow, CancellationToken.None);

        removed.Should().Be(4, "expired today, cancelled, deleted, and moved to another plan");
        (await ReadGrantIdsAsync()).Should().BeEquivalentTo([kept, keptPastDue]);
    }

    [TestMethod]
    public async Task RenewalJob_WhenStarted_RunsAPassAtOnce()
    {
        _host.EnsureAvailable();
        var plan = await SeedPlanAsync();
        var overdue = await SeedSubscriptionAsync(plan, Today);
        var spent = await SeedGrantAsync(await SeedSubscriptionAsync(plan, Today.AddDays(10)), plan, Today);
        var job = new SubscriptionRenewalJob(_host.Factory.Services, NullLogger<SubscriptionRenewalJob>.Instance, TimeProvider.System);

        await job.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => await ReadStatusAsync(overdue) == "past_due" && (await ReadGrantIdsAsync()).Count == 0);
        await job.StopAsync(CancellationToken.None);

        job.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        (await ReadGrantIdsAsync()).Should().NotContain(spent);
    }

    [TestMethod]
    public async Task TrialJob_ExpiresOnlyEndedTrials()
    {
        _host.EnsureAvailable();
        var plan = await SeedPlanAsync();
        var far = Today.AddDays(30);
        var ended = await SeedSubscriptionAsync(plan, far, trialEndsAt: DateTime.UtcNow.AddMinutes(-1));
        var running = await SeedSubscriptionAsync(plan, far, trialEndsAt: DateTime.UtcNow.AddDays(1));
        var paid = await SeedSubscriptionAsync(plan, far);
        var cancelled = await SeedSubscriptionAsync(plan, far, status: "cancelled", trialEndsAt: DateTime.UtcNow.AddDays(-1));
        var job = new TrialExpiryJob(_host.Factory.Services, NullLogger<TrialExpiryJob>.Instance, TimeProvider.System);

        await job.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => await ReadStatusAsync(ended) == "expired");
        await job.StopAsync(CancellationToken.None);

        job.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        (await ReadStatusAsync(running)).Should().Be("active");
        (await ReadStatusAsync(paid)).Should().Be("active");
        (await ReadStatusAsync(cancelled)).Should().Be("cancelled");
    }

    private static async Task<(int PastDue, int Expired)> RunRenewalPassAsync()
    {
        using var scope = _host.Factory.Services.CreateScope();
        return await SubscriptionRenewalJob.RunAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), DateTime.UtcNow, CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the job runs a pass as soon as it starts");
            await Task.Delay(50);
        }
    }

    private static async Task<Guid> SeedPlanAsync()
    {
        var planId = Guid.NewGuid();
        var suffix = planId.ToString("N")[..8];

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO subscription_plans
                (id, name, tier, description, monthly_price_usd, annual_price_usd, is_active, sort_order)
            VALUES (@id, @name, @tier, 'Fixture plan', 9.99, 99.99, true, 1);
            """,
            connection);
        command.Parameters.AddWithValue("id", planId);
        command.Parameters.AddWithValue("name", "Job plan " + suffix);
        command.Parameters.AddWithValue("tier", "job-" + suffix);
        await command.ExecuteNonQueryAsync();

        return planId;
    }

    private static async Task<Guid> SeedSubscriptionAsync(
        Guid planId,
        DateOnly renewalDate,
        string status = "active",
        DateTime? trialEndsAt = null,
        DateTime? deletedAt = null)
    {
        var account = await TestAccount.SignInAsync(_host, TestAccount.Unique("job") + "@apcs.test");
        var subscriptionId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO subscriptions
                (id, user_id, plan_id, billing_cycle, monthly_price_usd, annual_price_usd, status,
                 start_date, renewal_date, trial_ends_at, deleted_at, auto_renew, created_at)
            VALUES
                (@id, @userId, @planId, 'monthly', 9.99, 99.99, @status,
                 @startDate, @renewalDate, @trialEndsAt, @deletedAt, true, now());
            """,
            connection);
        command.Parameters.AddWithValue("id", subscriptionId);
        command.Parameters.AddWithValue("userId", account.UserId);
        command.Parameters.AddWithValue("planId", planId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("startDate", renewalDate.AddMonths(-1));
        command.Parameters.AddWithValue("renewalDate", renewalDate);
        command.Parameters.AddWithValue("trialEndsAt", (object?)trialEndsAt ?? DBNull.Value);
        command.Parameters.AddWithValue("deletedAt", (object?)deletedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();

        return subscriptionId;
    }

    private static async Task<Guid> SeedGrantAsync(Guid subscriptionId, Guid planId, DateOnly expiresOn, string featureCode = "lifestyle_mockup")
    {
        var grantId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using (var seedFeature = new NpgsqlCommand(
            """
            INSERT INTO features (code, label, group_name, kind)
            VALUES (@featureCode, @featureCode, 'Test', 'flag')
            ON CONFLICT (code) DO NOTHING;
            """,
            connection))
        {
            seedFeature.Parameters.AddWithValue("featureCode", featureCode);
            await seedFeature.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO subscription_feature_grants
                (id, subscription_id, plan_id, feature_code, is_enabled, expires_on, reason)
            VALUES (@id, @subscriptionId, @planId, @featureCode, true, @expiresOn, 'feature_removed');
            """,
            connection);
        command.Parameters.AddWithValue("id", grantId);
        command.Parameters.AddWithValue("subscriptionId", subscriptionId);
        command.Parameters.AddWithValue("planId", planId);
        command.Parameters.AddWithValue("featureCode", featureCode);
        command.Parameters.AddWithValue("expiresOn", expiresOn);
        await command.ExecuteNonQueryAsync();

        return grantId;
    }

    private static async Task<string> ReadStatusAsync(Guid subscriptionId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT status FROM subscriptions WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", subscriptionId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<Guid>> ReadGrantIdsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM subscription_feature_grants", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static string ConnectionString() =>
        TestEnvironment.Get("ConnectionStrings:DefaultConnection")
        ?? throw new InvalidOperationException("The host published no PostgreSQL connection string.");
}
