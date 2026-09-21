using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class DesignTemplateRedisCacheTests
{
    [TestMethod]
    public async Task DesignTemplateAggregates_Redis_IsolateTenantsApplyTtlsAndRemoveExactKey()
    {
        var connectionString = Environment.GetEnvironmentVariable("APCS_TEST_REDIS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Set APCS_TEST_REDIS_CONNECTION_STRING to run the opt-in Redis test.");
            return;
        }

        var instanceName = $"APCS:test:{Guid.NewGuid():N}:";
        var sellerA = Guid.NewGuid();
        var sellerB = Guid.NewGuid();
        const string systemKey = "design-templates:v1:system:all";
        var sellerAKey = $"design-templates:v1:user:{sellerA:N}:all";
        var sellerBKey = $"design-templates:v1:user:{sellerB:N}:all";
        var logicalKeys = new[] { systemKey, sellerAKey, sellerBKey };

        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = instanceName;
        });
        await using var provider = services.BuildServiceProvider();
        var distributedCache = provider.GetRequiredService<IDistributedCache>();
        var cache = new RedisCacheService(
            distributedCache,
            Microsoft.Extensions.Options.Options.Create(new RedisOptions
            {
                ConnectionString = connectionString,
                InstanceName = instanceName,
                DefaultExpirationMinutes = 10
            }));
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var database = multiplexer.GetDatabase();

        try
        {
            await cache.SetAsync(systemKey, Payload("System", true), TimeSpan.FromMinutes(60));
            await cache.SetAsync(sellerAKey, Payload("Seller A", false), TimeSpan.FromMinutes(5));
            await cache.SetAsync(sellerBKey, Payload("Seller B", false), TimeSpan.FromMinutes(5));

            var system = await cache.GetAsync<TemplateCachePayload[]>(systemKey);
            var personalA = await cache.GetAsync<TemplateCachePayload[]>(sellerAKey);
            var personalB = await cache.GetAsync<TemplateCachePayload[]>(sellerBKey);

            Assert.IsNotNull(system);
            Assert.IsNotNull(personalA);
            Assert.IsNotNull(personalB);
            Assert.AreEqual("System", system.Single().Name);
            Assert.AreEqual("Seller A", personalA.Single().Name);
            Assert.AreEqual("Seller B", personalB.Single().Name);
            CollectionAssert.AreEqual(new[] { "subject", "prompt" }, personalA.Single().Examples);

            var systemTtl = await database.KeyTimeToLiveAsync(instanceName + systemKey);
            var personalTtl = await database.KeyTimeToLiveAsync(instanceName + sellerAKey);
            Assert.IsNotNull(systemTtl);
            Assert.IsNotNull(personalTtl);
            Assert.IsTrue(systemTtl > TimeSpan.FromMinutes(55) && systemTtl <= TimeSpan.FromMinutes(60));
            Assert.IsTrue(personalTtl > TimeSpan.FromMinutes(4) && personalTtl <= TimeSpan.FromMinutes(5));

            await cache.RemoveAsync(sellerAKey);

            Assert.IsNull(await cache.GetAsync<TemplateCachePayload[]>(sellerAKey));
            Assert.IsNotNull(await cache.GetAsync<TemplateCachePayload[]>(systemKey));
            Assert.IsNotNull(await cache.GetAsync<TemplateCachePayload[]>(sellerBKey));
        }
        finally
        {
            foreach (var key in logicalKeys)
            {
                await database.KeyDeleteAsync(instanceName + key);
            }
        }
    }

    private static TemplateCachePayload[] Payload(string name, bool isSystem) =>
        [new(Guid.NewGuid(), name, isSystem, ["subject", "prompt"])];

    private sealed record TemplateCachePayload(
        Guid Id,
        string Name,
        bool IsSystemTemplate,
        string[] Examples);
}
