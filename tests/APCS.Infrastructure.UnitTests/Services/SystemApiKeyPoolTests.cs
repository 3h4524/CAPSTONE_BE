using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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
        var pool = Pool(new Dictionary<string, string?> { ["SystemApiKeys:MiniMax:Keys"] = "mm-1;mm-2" });

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
    public void Read_TakesThePoolTogetherWithTheSingleKeysConfiguredBeforeIt()
    {
        var options = Options(new Dictionary<string, string?>
        {
            ["SystemApiKeys:Ideogram:Keys"] = " key-a ; key-b,key-a ",
            ["SystemApiKeys:Ideogram:DailyLimitPerKey"] = "500",
            ["IDEOGRAM_API_KEY"] = "legacy-ideogram",
            ["MiniMax:ApiKey"] = "legacy-minimax",
            ["SystemApiKeys:MiniMax:MaxConcurrentPerKey"] = "2",
            ["SystemApiKeys:MiniMax:Strategy"] = " rotate ",
        });

        var ideogram = options.Providers["ideogram"];
        ideogram.Keys.Should().Equal("key-a", "key-b", "legacy-ideogram");
        ideogram.DailyLimitPerKey.Should().Be(500);
        ideogram.MaxConcurrentPerKey.Should().Be(SystemApiKeysOptions.DefaultIdeogramConcurrency);
        ideogram.Strategy.Should().BeNull("the keys are filled in order unless told otherwise");
        var miniMax = options.Providers["minimax"];
        miniMax.Keys.Should().Equal("legacy-minimax");
        miniMax.DailyLimitPerKey.Should().BeNull("no limit was set");
        miniMax.MaxConcurrentPerKey.Should().Be(2);
        miniMax.Strategy.Should().Be("rotate");
    }

    private static Dictionary<string, string?> Ideogram(string[] keys, int? dailyLimit = null, int? concurrency = null, string? strategy = null)
    {
        var settings = new Dictionary<string, string?> { ["SystemApiKeys:Ideogram:Keys"] = string.Join(';', keys) };
        if (dailyLimit is not null) settings["SystemApiKeys:Ideogram:DailyLimitPerKey"] = dailyLimit.ToString();
        if (concurrency is not null) settings["SystemApiKeys:Ideogram:MaxConcurrentPerKey"] = concurrency.ToString();
        if (strategy is not null) settings["SystemApiKeys:Ideogram:Strategy"] = strategy;
        return settings;
    }

    private static SystemApiKeysOptions Options(Dictionary<string, string?> settings)
    {
        var options = new SystemApiKeysOptions();
        SystemApiKeysOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), options);
        return options;
    }

    private static SystemApiKeyPool Pool(Dictionary<string, string?> settings, InMemorySystemApiKeyUsageStore? store = null,
        TimeProvider? time = null) =>
        new(Microsoft.Extensions.Options.Options.Create(Options(settings)), store ?? new InMemorySystemApiKeyUsageStore(),
            time ?? new FakeTimeProvider(Noon), NullLogger<SystemApiKeyPool>.Instance);
}
