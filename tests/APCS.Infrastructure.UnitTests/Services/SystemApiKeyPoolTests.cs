using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Services;
using APCS.Infrastructure.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class SystemApiKeyPoolTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AcquireAsync_ByDefault_UsesTheFirstKeyUntilItIsUsedUp_ThenTheNext()
    {
        var pool = Pool(Ideogram(["key-a", "key-b"], dailyLimit: 3));

        var used = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            await using var lease = await pool.AcquireAsync("ideogram");
            used.Add(lease!.ApiKey);
        }

        used.Should().Equal("key-a", "key-a", "key-a", "key-b", "key-b", "key-b");
        (await pool.AcquireAsync("ideogram")).Should().BeNull();
    }

    [TestMethod]
    public async Task AcquireAsync_Filling_GoesOnToTheNextKeyWhileTheFirstHasEveryPlaceTaken()
    {
        var pool = Pool(Ideogram(["key-a", "key-b"], concurrency: 1));

        await using var first = await pool.AcquireAsync("ideogram");
        await using var second = await pool.AcquireAsync("ideogram");

        (first!.ApiKey, second!.ApiKey).Should().Be(("key-a", "key-b"));
        await first.DisposeAsync();
        await using var third = await pool.AcquireAsync("ideogram");
        third!.ApiKey.Should().Be("key-a", "the first key is back in use once it has a place again");
    }

    [TestMethod]
    [DataRow("sideways")]
    [DataRow("FILL")]
    public async Task AcquireAsync_FillOrAnUnknownStrategy_UsesTheKeysInOrder(string strategy)
    {
        var pool = Pool(Ideogram(["key-a", "key-b"], strategy: strategy));

        for (var i = 0; i < 3; i++)
        {
            await using var lease = await pool.AcquireAsync("ideogram");
            lease!.ApiKey.Should().Be("key-a");
        }
    }

    [TestMethod]
    public async Task AcquireAsync_PassesOverTheKeysAskedToSkip_AndGivesNothingWhenOnlyThoseAreLeft()
    {
        var pool = Pool(Ideogram(["key-a", "key-b"]));
        await using var probe = await pool.AcquireAsync("ideogram");
        var keyA = probe!.KeyId;
        await probe.RefundAsync();
        await probe.DisposeAsync();

        await using var other = await pool.AcquireAsync("ideogram", new HashSet<string> { keyA });
        var none = await pool.AcquireAsync("ideogram", new HashSet<string> { keyA, other!.KeyId });

        other.ApiKey.Should().Be("key-b");
        none.Should().BeNull("every key was asked to be skipped");
    }

    [TestMethod]
    public async Task AcquireAsync_Rotating_TakesTheKeysInTurn()
    {
        var pool = Pool(Ideogram(["key-a", "key-b"], strategy: "Rotate"));

        var used = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            await using var lease = await pool.AcquireAsync("ideogram");
            used.Add(lease!.ApiKey);
        }

        used.Should().Equal("key-a", "key-b", "key-a", "key-b");
    }

    [TestMethod]
    public async Task AcquireAsync_PassesOverAKeyUsedUpForTheDay_AndGivesNothingOnceAllAre()
    {
        var store = new InMemorySystemApiKeyUsageStore();
        var pool = Pool(Ideogram(["key-a", "key-b"], dailyLimit: 2, strategy: "rotate"), store);

        var used = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            await using var lease = await pool.AcquireAsync("ideogram");
            used.Add(lease!.ApiKey);
        }

        used.Should().HaveCount(4).And.Contain(["key-a", "key-a", "key-b", "key-b"]);
        (await pool.AcquireAsync("ideogram")).Should().BeNull("both keys made their two images today");
    }

    [TestMethod]
    public async Task AcquireAsync_StartsAgainTheNextDay()
    {
        var time = new FakeTimeProvider(Noon);
        var pool = Pool(Ideogram(["key-a"], dailyLimit: 1), time: time);
        await using (await pool.AcquireAsync("ideogram")) { }
        (await pool.AcquireAsync("ideogram")).Should().BeNull();

        time.Advance(TimeSpan.FromHours(12));

        (await pool.AcquireAsync("ideogram")).Should().NotBeNull();
    }

    [TestMethod]
    public async Task RefundAsync_GivesTheUseBack()
    {
        var store = new InMemorySystemApiKeyUsageStore();
        var pool = Pool(Ideogram(["key-a"], dailyLimit: 1), store);

        await using (var lease = await pool.AcquireAsync("ideogram"))
            await lease!.RefundAsync();

        (await pool.AcquireAsync("ideogram")).Should().NotBeNull("the refunded use is free again");
    }

    [TestMethod]
    public async Task DisableForTodayAsync_PassesTheKeyOverForTheRestOfTheDay()
    {
        var pool = Pool(Ideogram(["key-a", "key-b"]));

        await using (var refused = await pool.AcquireAsync("ideogram"))
            await refused!.DisableForTodayAsync("Ideogram returned 402: out of credits");

        for (var i = 0; i < 3; i++)
        {
            await using var lease = await pool.AcquireAsync("ideogram");
            lease!.ApiKey.Should().Be("key-b");
        }
    }

    [TestMethod]
    public async Task AcquireAsync_WaitsForAPlaceOnceEveryKeyHasAsManyRequestsAsItTakes()
    {
        var pool = Pool(Ideogram(["key-a"], concurrency: 1));
        var first = await pool.AcquireAsync("ideogram");

        var second = pool.AcquireAsync("ideogram");
        await Task.Delay(50);
        second.IsCompleted.Should().BeFalse("the only key is busy");

        await first!.DisposeAsync();
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }

    [TestMethod]
    public async Task FindKey_FindsTheKeyALeaseHandedOut_ByItsId()
    {
        var pool = Pool(SystemApiKeyTestPools.Provider("minimax", ["mm-1", "mm-2"]));

        await using var lease = await pool.AcquireAsync("minimax");

        lease!.KeyId.Should().HaveLength(12).And.NotContain(lease.ApiKey);
        pool.FindKey("minimax", lease.KeyId).Should().Be(lease.ApiKey);
        pool.FindKey("minimax", "000000000000").Should().BeNull();
    }

    [TestMethod]
    public async Task AProviderWithNoKey_HasNothingToHandOut()
    {
        var pool = Pool(Ideogram(["key-a"]));

        pool.HasKeys("minimax").Should().BeFalse();
        pool.AnyKey("minimax").Should().BeNull();
        (await pool.AcquireAsync("minimax")).Should().BeNull();
    }

    [TestMethod]
    public async Task Refresh_TakesUpAKeyAnAdminAdded_AndDropsOneAnAdminDeleted()
    {
        var source = new FakeSystemApiKeySource().Set(SystemApiKeyTestPools.Provider("ideogram", ["key-a"]));
        var pool = SystemApiKeyTestPools.Create(source, time: new FakeTimeProvider(Noon));
        pool.HasKeys("minimax").Should().BeFalse();

        source.Set(SystemApiKeyTestPools.Provider("ideogram", ["key-b"]));
        source.Set(SystemApiKeyTestPools.Provider("minimax", ["mm-1"]));
        await pool.RefreshAsync();

        await using var lease = await pool.AcquireAsync("ideogram");
        lease!.ApiKey.Should().Be("key-b");
        pool.FindKey("ideogram", SystemApiKeyPool.KeyIdOf("key-a")).Should().BeNull("the key was deleted");
        pool.AnyKey("minimax").Should().Be("mm-1");
    }

    [TestMethod]
    public async Task Refresh_KeepsTheRequestsAlreadyRunningOnAKey_WithinItsLimitAtATime()
    {
        var source = new FakeSystemApiKeySource().Set(SystemApiKeyTestPools.Provider("ideogram", ["key-a"], concurrency: 1));
        var pool = SystemApiKeyTestPools.Create(source, time: new FakeTimeProvider(Noon));
        var running = await pool.AcquireAsync("ideogram");

        source.Set(SystemApiKeyTestPools.Provider("ideogram", ["key-a", "key-b"], concurrency: 1));
        await pool.RefreshAsync();
        await using var next = await pool.AcquireAsync("ideogram");

        next!.ApiKey.Should().Be("key-b", "key-a still has its one request running after the read");
        await running!.DisposeAsync();
        await using var again = await pool.AcquireAsync("ideogram");
        again!.ApiKey.Should().Be("key-a");
    }

    [TestMethod]
    public async Task AKeySwitchedOff_IsNotHandedOut_ButAVideoItStartedCanStillFindIt()
    {
        var source = new FakeSystemApiKeySource().Set(new SystemApiKeyProviderConfig("minimax", null, null, null,
            [new SystemApiKeyConfig("mm-off", false), new SystemApiKeyConfig("mm-on", true)]));
        var pool = SystemApiKeyTestPools.Create(source, time: new FakeTimeProvider(Noon));

        await using var lease = await pool.AcquireAsync("minimax");

        lease!.ApiKey.Should().Be("mm-on");
        pool.AnyKey("minimax").Should().Be("mm-on");
        pool.FindKey("minimax", SystemApiKeyPool.KeyIdOf("mm-off")).Should().Be("mm-off");
    }

    [TestMethod]
    public async Task AProviderWithEveryKeySwitchedOff_HasNoKeys()
    {
        var pool = SystemApiKeyTestPools.Create([new SystemApiKeyProviderConfig("ideogram", null, null, null,
            [new SystemApiKeyConfig("key-a", false)])]);

        pool.HasKeys("ideogram").Should().BeFalse();
        (await pool.AcquireAsync("ideogram")).Should().BeNull();
    }

    [TestMethod]
    public async Task ARefreshThatFails_KeepsTheKeysReadLast()
    {
        var source = new FakeSystemApiKeySource().Set(SystemApiKeyTestPools.Provider("ideogram", ["key-a"]));
        var pool = SystemApiKeyTestPools.Create(source, time: new FakeTimeProvider(Noon));
        source.Failure = new InvalidOperationException("the database is away");

        var refresh = () => pool.RefreshAsync();

        await refresh.Should().ThrowAsync<InvalidOperationException>();
        await using var lease = await pool.AcquireAsync("ideogram");
        lease!.ApiKey.Should().Be("key-a");
    }

    [TestMethod]
    public async Task APoolNotReadYet_ReadsTheKeysOnItsFirstRequest()
    {
        var source = new FakeSystemApiKeySource().Set(SystemApiKeyTestPools.Provider("ideogram", ["key-a"]));
        var pool = new SystemApiKeyPool(source, new InMemorySystemApiKeyUsageStore(), new FakeTimeProvider(Noon),
            NullLogger<SystemApiKeyPool>.Instance);

        await using var lease = await pool.AcquireAsync("ideogram");

        lease!.ApiKey.Should().Be("key-a");
        source.Loads.Should().Be(1);
    }

    [TestMethod]
    public async Task Refresher_ReadsTheKeysAgainStraightAfterAnAdminsChange()
    {
        var source = new FakeSystemApiKeySource().Set(SystemApiKeyTestPools.Provider("ideogram", ["key-a"]));
        var pool = new SystemApiKeyPool(source, new InMemorySystemApiKeyUsageStore(), new FakeTimeProvider(Noon),
            NullLogger<SystemApiKeyPool>.Instance);
        using var refresher = new SystemApiKeyPoolRefresher(pool, NullLogger<SystemApiKeyPoolRefresher>.Instance);

        await refresher.StartAsync(CancellationToken.None);
        pool.IsLoaded.Should().BeTrue("the keys are read before the server takes requests");
        source.Set(SystemApiKeyTestPools.Provider("ideogram", ["key-b"]));
        pool.Invalidate();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (pool.AnyKey("ideogram") != "key-b" && DateTime.UtcNow < deadline) await Task.Delay(10);
        await refresher.StopAsync(CancellationToken.None);

        pool.AnyKey("ideogram").Should().Be("key-b");
    }

    [TestMethod]
    public async Task UsageToday_ShowsTheUsesAndTheKeysRefused_AndARefusalCanBeCleared()
    {
        var pool = Pool(Ideogram(["key-a", "key-b"]));
        await using (var lease = await pool.AcquireAsync("ideogram")) { }
        await using (var refused = await pool.AcquireAsync("ideogram"))
            await refused!.DisableForTodayAsync("Ideogram returned 402: out of credits");
        var keyA = SystemApiKeyPool.KeyIdOf("key-a");

        var usage = await pool.GetUsageTodayAsync("ideogram", [keyA, SystemApiKeyPool.KeyIdOf("key-b")]);
        usage[keyA].Should().Be(new SystemApiKeyUsageToday(1, "Ideogram returned 402: out of credits"),
            "the refused use was given back, and fill went back to the first key");

        await pool.ClearRefusedTodayAsync("ideogram", keyA);

        (await pool.GetUsageTodayAsync("ideogram", [keyA]))[keyA].RefusedReason.Should().BeNull();
        await using var back = await pool.AcquireAsync("ideogram");
        back!.ApiKey.Should().Be("key-a", "the key is in use again");
    }

    private static SystemApiKeyProviderConfig Ideogram(string[] keys, int? dailyLimit = null, int? concurrency = null, string? strategy = null) =>
        SystemApiKeyTestPools.Provider("ideogram", keys, dailyLimit, concurrency, strategy);

    private static SystemApiKeyPool Pool(SystemApiKeyProviderConfig provider, InMemorySystemApiKeyUsageStore? store = null,
        TimeProvider? time = null) =>
        SystemApiKeyTestPools.Create([provider], store, time ?? new FakeTimeProvider(Noon));
}
