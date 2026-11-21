using System.Diagnostics;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using StackExchange.Redis;

namespace APCS.IntegrationTests.ListingStreams;

[TestClass]
public sealed class RedisListingStreamBufferTests
{
    private const string Prefix = "APCS:listing-buffer-test:";

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

    private static RedisListingStreamBuffer Buffer()
    {
        if (_redis is null)
            Assert.Inconclusive(_fixture.UnavailableReason ?? string.Empty);
        return new RedisListingStreamBuffer(_redis, Microsoft.Extensions.Options.Options.Create(new RedisOptions { ConnectionString = "unused", InstanceName = Prefix }));
    }

    [TestMethod]
    public async Task AppendAsync_KeysTheStreamByUserAndKeepsItLiveFor15MinutesAndEndedFor10()
    {
        var buffer = Buffer();
        var userId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        var key = $"{Prefix}listing:stream:{userId:D}:{streamId:D}";

        await buffer.AppendAsync(userId, streamId, "delta", """{"text":"a"}""");
        var live = await _redis!.GetDatabase().KeyTimeToLiveAsync(key);
        await buffer.AppendAsync(userId, streamId, "end", "{}");
        var ended = await _redis.GetDatabase().KeyTimeToLiveAsync(key);

        live.Should().BeGreaterThan(TimeSpan.FromMinutes(14)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
        ended.Should().BeGreaterThan(TimeSpan.FromMinutes(9)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
    }

    [TestMethod]
    public async Task ReadAfterAsync_ReadsOnlyTheOwnersStream()
    {
        var buffer = Buffer();
        var userId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        var first = await buffer.AppendAsync(userId, streamId, "start", """{"model":"m"}""");
        var second = await buffer.AppendAsync(userId, streamId, "end", "{}");

        var all = await buffer.ReadAfterAsync(userId, streamId, null);
        var later = await buffer.ReadAfterAsync(userId, streamId, first);
        var foreign = await buffer.ReadAfterAsync(Guid.NewGuid(), streamId, null);

        all.Select(entry => entry.Id).Should().Equal(first, second);
        later.Should().ContainSingle().Which.Type.Should().Be("end");
        foreign.Should().BeEmpty();
        (await buffer.ReadLastAsync(userId, streamId))!.Id.Should().Be(second);
        (await buffer.ReadLastAsync(Guid.NewGuid(), streamId)).Should().BeNull();
        (await buffer.ExistsAsync(userId, streamId)).Should().BeTrue();
    }

    [TestMethod]
    public async Task WatchAppendsAsync_IsWokenByTheNextAppend()
    {
        var buffer = Buffer();
        var userId = Guid.NewGuid();
        var streamId = Guid.NewGuid();
        await using var signal = await buffer.WatchAppendsAsync(userId, streamId);
        var clock = Stopwatch.StartNew();

        var waiting = signal.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await buffer.AppendAsync(userId, streamId, "start", "{}");
        await waiting;

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
