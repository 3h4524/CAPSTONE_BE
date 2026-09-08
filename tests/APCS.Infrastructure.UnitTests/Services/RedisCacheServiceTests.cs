using System.Text;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class RedisCacheServiceTests
{
    [TestMethod]
    public async Task GetAsync_WhenCacheMisses_ReturnsDefault()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(candidate => candidate.GetAsync("key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await CreateService(cache).GetAsync<CacheValue>("key");

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetAsync_WhenValueExists_DeserializesWebJson()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(candidate => candidate.GetAsync("key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("{\"name\":\"cached\",\"count\":3}"));

        var result = await CreateService(cache).GetAsync<CacheValue>("key");

        result.Should().BeEquivalentTo(new CacheValue("cached", 3));
    }

    [TestMethod]
    public async Task SetAsync_WithoutExpiration_SerializesAndUsesConfiguredDefault()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        byte[]? savedBytes = null;
        DistributedCacheEntryOptions? savedOptions = null;
        var cache = new Mock<IDistributedCache>();
        cache.Setup(candidate => candidate.SetAsync(
                "key",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                cancellationToken))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, bytes, options, _) =>
                {
                    savedBytes = bytes;
                    savedOptions = options;
                })
            .Returns(Task.CompletedTask);

        await CreateService(cache).SetAsync("key", new CacheValue("value", 2), null, cancellationToken);

        Encoding.UTF8.GetString(savedBytes!).Should().Be("{\"name\":\"value\",\"count\":2}");
        savedOptions!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(10));
    }

    [TestMethod]
    public async Task SetAsync_WithCustomExpiration_UsesCustomValue()
    {
        DistributedCacheEntryOptions? savedOptions = null;
        var cache = new Mock<IDistributedCache>();
        cache.Setup(candidate => candidate.SetAsync(
                "key",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, _, options, _) => savedOptions = options)
            .Returns(Task.CompletedTask);

        await CreateService(cache).SetAsync("key", new CacheValue("value", 2), TimeSpan.FromSeconds(30));

        savedOptions!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromSeconds(30));
    }

    [TestMethod]
    public async Task SetAsync_WithNullValue_DoesNotWrite()
    {
        var cache = new Mock<IDistributedCache>();

        await CreateService(cache).SetAsync<CacheValue>("key", null!);

        cache.Verify(candidate => candidate.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RemoveAsync_WithValidKey_DelegatesToCache()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var cache = new Mock<IDistributedCache>();
        cache.Setup(candidate => candidate.RemoveAsync("key", cancellationToken)).Returns(Task.CompletedTask);

        await CreateService(cache).RemoveAsync("key", cancellationToken);

        cache.Verify(candidate => candidate.RemoveAsync("key", cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task RemoveByPrefixAsync_AlwaysThrowsNotSupported()
    {
        var act = () => CreateService(new Mock<IDistributedCache>()).RemoveByPrefixAsync("seller:");

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [TestMethod]
    public async Task SetAndRemove_WithBlankKey_Throw()
    {
        var service = CreateService(new Mock<IDistributedCache>());

        var set = () => service.SetAsync(" ", new CacheValue("value", 1));
        var remove = () => service.RemoveAsync("");

        await set.Should().ThrowAsync<ArgumentException>();
        await remove.Should().ThrowAsync<ArgumentException>();
    }

    private static RedisCacheService CreateService(Mock<IDistributedCache> cache) => new(
        cache.Object,
        Microsoft.Extensions.Options.Options.Create(new RedisOptions
        {
            ConnectionString = "localhost:6379",
            DefaultExpirationMinutes = 10
        }));

    private sealed record CacheValue(string Name, int Count);
}
