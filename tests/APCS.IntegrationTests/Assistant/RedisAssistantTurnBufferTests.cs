using System.Diagnostics;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using StackExchange.Redis;

namespace APCS.IntegrationTests.Assistant;

[TestClass]
public sealed class RedisAssistantTurnBufferTests
{
    private const string Prefix = "APCS:buffer-test:";

    private static PostgresFixture _fixture = null!;
    private static ConnectionMultiplexer? _redis;

    [ClassInitialize]
    public static async Task ConnectAsync(TestContext testContext)
    {
        _fixture = await PostgresFixture.AcquireAsync();
        if (_fixture.IsAvailable)
            _redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisConnectionString!);
    }

    [ClassCleanup]
    public static async Task DisconnectAsync()
    {
        if (_redis is not null)
            await _redis.DisposeAsync();
        await PostgresFixture.ReleaseAsync();
    }

    private static RedisAssistantTurnBuffer Buffer()
    {
        if (_redis is null)
            Assert.Inconclusive(_fixture.UnavailableReason ?? string.Empty);
        return new RedisAssistantTurnBuffer(_redis, Microsoft.Extensions.Options.Options.Create(new RedisOptions { ConnectionString = "unused", InstanceName = Prefix }));
    }

    [TestMethod]
    public async Task AppendAsync_KeepsALiveTurnFor15MinutesAndAFinishedOneFor10()
    {
        var buffer = Buffer();
        var turnId = Guid.NewGuid();
        var key = $"{Prefix}assistant:turn:{turnId:D}";

        await buffer.AppendAsync(turnId, "delta", """{"text":"a"}""");
        var live = await _redis!.GetDatabase().KeyTimeToLiveAsync(key);
        await buffer.AppendAsync(turnId, "done", "{}");
        var finished = await _redis.GetDatabase().KeyTimeToLiveAsync(key);

        live.Should().BeGreaterThan(TimeSpan.FromMinutes(14)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
        finished.Should().BeGreaterThan(TimeSpan.FromMinutes(9)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        (await _redis.GetDatabase().KeyTypeAsync(key)).Should().Be(RedisType.Stream);
    }

    [TestMethod]
    public async Task ReadAfterAsync_ReturnsTypeAndDataOfTheEntriesAfterTheGivenId()
    {
        var buffer = Buffer();
        var turnId = Guid.NewGuid();
        var first = await buffer.AppendAsync(turnId, "conversation", """{"conversationId":"c"}""");
        var second = await buffer.AppendAsync(turnId, "delta", """{"text":"hi"}""");

        var all = await buffer.ReadAfterAsync(turnId, null);
        var later = await buffer.ReadAfterAsync(turnId, first);
        var none = await buffer.ReadAfterAsync(turnId, second);

        all.Select(entry => entry.Id).Should().Equal(first, second);
        later.Should().ContainSingle().Which.Should().Match<APCS.Application.Abstractions.Caching.AssistantTurnEntry>(entry =>
            entry.Id == second && entry.Type == "delta" && entry.Data == """{"text":"hi"}""");
        none.Should().BeEmpty();
        (await buffer.ExistsAsync(turnId)).Should().BeTrue();
        (await buffer.ExistsAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [TestMethod]
    public async Task WatchAppendsAsync_IsWokenByTheNextAppend()
    {
        var buffer = Buffer();
        var turnId = Guid.NewGuid();
        await using var signal = await buffer.WatchAppendsAsync(turnId);
        var clock = Stopwatch.StartNew();

        var waiting = signal.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await buffer.AppendAsync(turnId, "delta", "{}");
        await waiting;

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task RequestStopAsync_IsSeenThroughTheKeyAndWakesTheStopWatcher()
    {
        var buffer = Buffer();
        var turnId = Guid.NewGuid();
        await using var signal = await buffer.WatchStopAsync(turnId);
        var clock = Stopwatch.StartNew();

        var waiting = signal.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        (await buffer.IsStopRequestedAsync(turnId)).Should().BeFalse();
        await buffer.RequestStopAsync(turnId);
        await waiting;

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        (await buffer.IsStopRequestedAsync(turnId)).Should().BeTrue();
        (await _redis!.GetDatabase().KeyTimeToLiveAsync($"{Prefix}assistant:turn:{turnId:D}:stop")).Should().NotBeNull();
    }

    [TestMethod]
    public async Task AppendAsync_TrimsTheStreamToAbout5000Entries()
    {
        var buffer = Buffer();
        var turnId = Guid.NewGuid();

        for (var index = 0; index < RedisAssistantTurnBuffer.MaxEntries + 300; index++)
            await buffer.AppendAsync(turnId, "delta", "{}");

        var length = await _redis!.GetDatabase().StreamLengthAsync($"{Prefix}assistant:turn:{turnId:D}");
        length.Should().BeInRange(RedisAssistantTurnBuffer.MaxEntries, RedisAssistantTurnBuffer.MaxEntries + 200);
    }
}
