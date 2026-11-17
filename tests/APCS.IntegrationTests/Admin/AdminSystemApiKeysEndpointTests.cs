using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Services;
using APCS.IntegrationTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.IntegrationTests.Admin;

/// <summary>
/// The platform key page against real PostgreSQL and Redis: a key encrypted with the ring kept in the database is read
/// back by the API's own pool, and the page shows it without ever returning it.
/// </summary>
[TestClass]
public sealed class AdminSystemApiKeysEndpointTests
{
    private const string Route = "/api/admin/system-api-keys";
    private const string RawKey = "ideogram-integration-key-4321";

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
    public async Task Get_AsASeller_IsForbidden()
    {
        _host.EnsureAvailable();
        using var client = _host.Factory.CreateAuthenticatedClient(TestAuth.MintToken([TestAuth.SellerRole]));

        using var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task AKeySavedEncrypted_IsUsedByThePool_AndShownMaskedOnThePage()
    {
        _host.EnsureAvailable();
        await SeedKeyAsync();
        var pool = _host.Factory.Services.GetRequiredService<SystemApiKeyPool>();

        await pool.RefreshAsync();
        using var client = _host.Factory.CreateAuthenticatedClient(TestAuth.MintToken([TestAuth.AdminRole]));
        using var response = await client.GetAsync(Route);

        pool.AnyKey(SystemApiKeyProviders.Ideogram).Should().Be(RawKey, "the key was decrypted with the ring in the database");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(RawKey);
        var page = JsonDocument.Parse(text).RootElement;
        var ideogram = page.GetProperty("providers").EnumerateArray().Single(provider => provider.GetProperty("provider").GetString() == "ideogram");
        var key = ideogram.GetProperty("keys").EnumerateArray().Single();
        key.GetProperty("maskedKey").GetString().Should().Be("****4321");
        key.GetProperty("readable").GetBoolean().Should().BeTrue();
        key.GetProperty("usedToday").GetInt32().Should().Be(0);
    }

    // The way an Admin's save leaves it: the provider's limits, and the key encrypted by the API's own secrets.
    private static async Task SeedKeyAsync()
    {
        using var scope = _host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISystemApiKeySecrets>();
        var now = DateTime.UtcNow;
        db.SystemApiKeyProviders.Add(new SystemApiKeyProvider
        {
            Provider = SystemApiKeyProviders.Ideogram, DailyLimitPerKey = 500, MaxConcurrentPerKey = 8, Strategy = "fill", UpdatedAt = now,
        });
        db.SystemApiKeys.Add(new SystemApiKey
        {
            Id = Guid.NewGuid(),
            Provider = SystemApiKeyProviders.Ideogram,
            Label = "Integration",
            KeyValueEncrypted = secrets.Protect(RawKey),
            KeyLast4 = "4321",
            KeyId = SystemApiKeyIds.Of(RawKey),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }
}
