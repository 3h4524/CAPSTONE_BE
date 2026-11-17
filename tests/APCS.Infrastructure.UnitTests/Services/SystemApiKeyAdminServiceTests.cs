using System.Security.Cryptography;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.Admin.SystemApiKeys;
using APCS.Application.Features.Admin.SystemApiKeys.Dtos.Request;
using APCS.Application.Features.Admin.SystemApiKeys.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class SystemApiKeyAdminServiceTests
{
    private const string RawKey = "ideogram-secret-key-9876";
    private static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid admin = Guid.NewGuid();
    private readonly Mock<IApiKeyCredentials> credentials = new();
    private readonly Mock<ISystemApiKeyPool> pool = new();
    private readonly Dictionary<string, SystemApiKeyUsageToday> usage = [];
    private readonly FakeTimeProvider time = new(Noon);
    private AppDbContext db = null!;

    [TestInitialize]
    public void SetUp()
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.SystemApiKeyProviders.AddRange(
            new SystemApiKeyProvider { Provider = "ideogram", DailyLimitPerKey = 500, MaxConcurrentPerKey = 8, Strategy = "fill", UpdatedAt = Noon.UtcDateTime },
            new SystemApiKeyProvider { Provider = "minimax", DailyLimitPerKey = 100, Strategy = "fill", UpdatedAt = Noon.UtcDateTime });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        credentials.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        pool.Setup(x => x.GetUsageTodayAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyCollection<string> ids, CancellationToken _) =>
                ids.Where(usage.ContainsKey).ToDictionary(id => id, id => usage[id]));
    }

    [TestCleanup]
    public void TearDown() => db.Dispose();

    [TestMethod]
    public async Task AddKey_TheProviderAccepts_SavesItEncrypted_AndNeverShowsOrRecordsTheKey()
    {
        var result = await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", " Main account ", $" {RawKey} "));

        result.IsSuccess.Should().BeTrue();
        var key = result.Value.Providers.Single(provider => provider.Provider == "ideogram").Keys.Single();
        (key.Label, key.MaskedKey, key.KeyId, key.IsActive, key.Readable).Should()
            .Be(("Main account", "****9876", SystemApiKeyIds.Of(RawKey), true, true));
        var row = await db.SystemApiKeys.AsNoTracking().SingleAsync();
        row.KeyValueEncrypted.Should().Be($"enc:{RawKey}");
        row.UpdatedBy.Should().Be(admin);
        System.Text.Json.JsonSerializer.Serialize(result.Value).Should().NotContain(RawKey);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync();
        (audit.ActionType, audit.ActorUserId).Should().Be(("AddSystemApiKey", admin));
        audit.NewValue.Should().Contain("****9876").And.NotContain(RawKey);
        pool.Verify(x => x.Invalidate(), Times.Once, "the keys are read again at once on this server");
    }

    [TestMethod]
    public async Task AddKey_TheProviderRefuses_SavesNothing()
    {
        credentials.Setup(x => x.ValidateAsync("minimax", RawKey, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("minimax", "Video", RawKey));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().StartWith("MiniMax did not accept this key");
        (await db.SystemApiKeys.AnyAsync()).Should().BeFalse();
        pool.Verify(x => x.Invalidate(), Times.Never);
    }

    [TestMethod]
    public async Task AddKey_AKeyAlreadyOnTheList_IsRefusedAsAConflict()
    {
        await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "First", RawKey));

        var again = await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "Second", RawKey));

        again.Error.Type.Should().Be(ErrorType.Conflict);
        (await db.SystemApiKeys.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task AddKey_AnUnknownProvider_IsAValidationError()
    {
        var result = await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("openai", "Other", RawKey));

        result.Error.Type.Should().Be(ErrorType.Validation);
        credentials.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateKey_SwitchesItOff_UnlessAnotherAdminChangedItSinceItWasRead()
    {
        var added = (await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "Main", RawKey))).Value;
        var read = added.Providers[0].Keys.Single();

        var off = await Service().UpdateKeyAsync(read.Id, new UpdateSystemApiKeyRequestDto("Main", false, read.UpdatedAt));
        var stale = await Service().UpdateKeyAsync(read.Id, new UpdateSystemApiKeyRequestDto("Renamed", true, read.UpdatedAt));

        off.Value.Providers[0].Keys.Single().IsActive.Should().BeFalse();
        stale.Error.Type.Should().Be(ErrorType.Conflict, "the key changed after this Admin read it");
        (await db.SystemApiKeys.AsNoTracking().SingleAsync()).Label.Should().Be("Main");
    }

    [TestMethod]
    public async Task DeleteKey_TakesItOffTheList_AndThrowsTheKeyAway()
    {
        var added = (await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "Main", RawKey))).Value;

        var result = await Service().DeleteKeyAsync(added.Providers[0].Keys.Single().Id);

        result.Value.Providers[0].Keys.Should().BeEmpty();
        var row = await db.SystemApiKeys.AsNoTracking().SingleAsync();
        (row.DeletedAt, row.KeyValueEncrypted, row.IsActive).Should().Be((time.GetUtcNow().UtcDateTime, string.Empty, false));
    }

    [TestMethod]
    public async Task Get_ShowsTodaysUse_AndAKeyThisServerCannotDecrypt()
    {
        await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "Main", RawKey));
        await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "Foreign", "ideogram-other-key-5555"));
        var foreign = await db.SystemApiKeys.SingleAsync(key => key.Label == "Foreign");
        foreign.KeyValueEncrypted = "from-another-ring";
        await db.SaveChangesAsync();
        usage[SystemApiKeyIds.Of(RawKey)] = new SystemApiKeyUsageToday(42, "Ideogram returned 402: out of credits");

        var page = (await Service().GetAsync()).Value;

        page.Providers.Select(provider => provider.Name).Should().Equal("Ideogram", "MiniMax");
        var keys = page.Providers[0].Keys;
        keys.Select(key => key.Label).Should().Equal("Main", "Foreign");
        (keys[0].UsedToday, keys[0].RefusedToday).Should().Be((42, "Ideogram returned 402: out of credits"));
        keys[1].Readable.Should().BeFalse();
    }

    [TestMethod]
    public async Task ClearRefused_PutsTheKeyBackInUse()
    {
        var added = (await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "Main", RawKey))).Value;

        await Service().ClearRefusedAsync(added.Providers[0].Keys.Single().Id);

        pool.Verify(x => x.ClearRefusedTodayAsync("ideogram", SystemApiKeyIds.Of(RawKey), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateProvider_SavesTheLimits_AndRefusesAStrategyItDoesNotKnow()
    {
        var page = (await Service().GetAsync()).Value;
        var read = page.Providers[0].UpdatedAt;

        var wrong = await Service().UpdateProviderAsync("ideogram", new UpdateSystemApiKeyProviderRequestDto(500, 8, "sideways", read));
        var saved = await Service().UpdateProviderAsync("ideogram", new UpdateSystemApiKeyProviderRequestDto(null, 4, "rotate", read));
        var stale = await Service().UpdateProviderAsync("ideogram", new UpdateSystemApiKeyProviderRequestDto(10, 4, "rotate", read));

        wrong.Error.Type.Should().Be(ErrorType.Validation);
        var ideogram = saved.Value.Providers[0];
        (ideogram.DailyLimitPerKey, ideogram.MaxConcurrentPerKey, ideogram.Strategy).Should().Be((null, 4, "rotate"));
        stale.Error.Type.Should().Be(ErrorType.Conflict);
        pool.Verify(x => x.Invalidate(), Times.Once);
    }

    [TestMethod]
    public async Task Reorder_ChangesTheOrderFillTakesTheKeysIn_OnlyWhenEveryKeyIsNamedOnce()
    {
        await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "First", RawKey));
        var page = (await Service().AddKeyAsync(new AddSystemApiKeyRequestDto("ideogram", "Second", "ideogram-other-key-5555"))).Value;
        var ids = page.Providers[0].Keys.Select(key => key.Id).ToList();

        var partial = await Service().ReorderAsync("ideogram", new ReorderSystemApiKeysRequestDto([ids[1]]));
        var swapped = await Service().ReorderAsync("ideogram", new ReorderSystemApiKeysRequestDto([ids[1], ids[0]]));

        partial.Error.Type.Should().Be(ErrorType.Validation);
        swapped.Value.Providers[0].Keys.Select(key => key.Label).Should().Equal("Second", "First");
    }

    [TestMethod]
    public async Task Get_OnADatabaseTheMigrationHasNotReached_SaysTheTablesAreMissingInsteadOfFailing()
    {
        var missing = new Mock<APCS.Application.Abstractions.Persistence.IRepository<SystemApiKeyProvider>>();
        missing.Setup(x => x.Query()).Throws(new MissingTable());

        var result = await Service(missing.Object).GetAsync();

        result.IsSuccess.Should().BeTrue();
        (result.Value.StorageReady, result.Value.Providers.Count).Should().Be((false, 0));
    }

    // What Npgsql throws for a table that does not exist.
    private sealed class MissingTable() : System.Data.Common.DbException("relation \"system_api_key_providers\" does not exist")
    {
        public override string SqlState => "42P01";
    }

    // A service per call, as per request: nothing an earlier call tracked is still attached.
    private SystemApiKeyAdminService Service(APCS.Application.Abstractions.Persistence.IRepository<SystemApiKeyProvider>? providers = null)
    {
        db.ChangeTracker.Clear();
        time.Advance(TimeSpan.FromSeconds(1));
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(admin);
        var secrets = new Mock<ISystemApiKeySecrets>();
        secrets.Setup(x => x.Protect(It.IsAny<string>())).Returns((string value) => $"enc:{value}");
        secrets.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) =>
            value.StartsWith("enc:", StringComparison.Ordinal) ? value[4..] : throw new CryptographicException("not this ring"));
        return new SystemApiKeyAdminService(
            new Repository<SystemApiKey>(db), providers ?? new Repository<SystemApiKeyProvider>(db), new Repository<AuditLog>(db), db,
            user.Object, time, secrets.Object, credentials.Object, pool.Object,
            new AddSystemApiKeyRequestDtoValidator(), new UpdateSystemApiKeyRequestDtoValidator(),
            new UpdateSystemApiKeyProviderRequestDtoValidator());
    }
}
