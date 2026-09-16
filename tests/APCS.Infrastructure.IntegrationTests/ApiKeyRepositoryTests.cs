using System.Data.Common;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class ApiKeyRepositoryTests
{
    [TestMethod]
    public async Task LockedWrites_PostgreSql_PersistAndFilterSoftDeletesAndOwners()
    {
        var connection = Environment.GetEnvironmentVariable("APCS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) { Assert.Inconclusive("Set APCS_TEST_CONNECTION_STRING to run PostgreSQL tests."); return; }
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.OpenConnectionAsync();
        // This session-local table shadows the application's unqualified table mapping.
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE api_keys (LIKE public.api_keys INCLUDING ALL)");
        var repository = new ApiKeyRepository(db);
        var owner = Guid.NewGuid();
        var row = new ApiKey { Id = Guid.NewGuid(), UserId = owner, ServiceProvider = "openai", AuthType = "api_key",
            KeyIdentifier = "Synthetic", KeyValueEncrypted = "synthetic-ciphertext", KeyLast4 = "1234", IsActive = true,
            LastCheckedAt = DateTime.UtcNow, LastCheckSucceeded = true };
        await using (var transaction = await repository.LockOwnerAsync(owner, CancellationToken.None))
        {
            await repository.AddAsync(row, true);
            await transaction.CommitAsync();
        }
        db.ChangeTracker.Clear();
        Assert.AreEqual(1, (await repository.ListOwnedAsync(owner, CancellationToken.None)).Count);
        Assert.AreEqual(0, (await repository.ListOwnedAsync(Guid.NewGuid(), CancellationToken.None)).Count);
        db.ChangeTracker.Clear();
        await using (var transaction = await repository.LockOwnerAsync(owner, CancellationToken.None))
        {
            var saved = (await repository.ListOwnedAsync(owner, CancellationToken.None)).Single();
            saved.DeletedAt = DateTime.UtcNow; saved.IsActive = false; saved.KeyValueEncrypted = "";
            await repository.UpdateAsync(saved, true);
            await transaction.CommitAsync();
        }
        db.ChangeTracker.Clear();
        Assert.AreEqual(0, (await repository.ListOwnedAsync(owner, CancellationToken.None)).Count);
        Assert.AreEqual(1, await db.ApiKeys.CountAsync());
    }

    [TestMethod]
    public async Task ListMetadataOwnedAsync_PostgreSql_FiltersOwnersAndDeletedRowsWithoutLoadingSecrets()
    {
        var connectionString = Environment.GetEnvironmentVariable("APCS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Set APCS_TEST_CONNECTION_STRING to run the opt-in PostgreSQL test.");
            return;
        }

        var audit = new QueryAudit();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString).AddInterceptors(audit).Options;
        await using var db = new AppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Session-local table shadows the unqualified api_keys mapping. No public rows are written.
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TEMP TABLE api_keys (LIKE public.api_keys INCLUDING ALL) ON COMMIT DROP");
            var owner = Guid.NewGuid();
            var otherOwner = Guid.NewGuid();
            var visible = Guid.NewGuid();
            var deleted = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pg_temp.api_keys
                    (id, user_id, service_provider, key_identifier, key_value_encrypted, key_last_4, deleted_at)
                VALUES
                    ({visible}, {owner}, 'openai', 'Visible', 'synthetic-not-a-real-credential', 'ABCD', NULL),
                    ({deleted}, {owner}, 'openai', 'Deleted', 'synthetic-not-a-real-credential', 'EFGH', CURRENT_TIMESTAMP),
                    ({foreign}, {otherOwner}, 'replicate', 'Other owner', 'synthetic-not-a-real-credential', 'IJKL', NULL)
                """);
            audit.Enabled = true;
            var repository = new ApiKeyRepository(db);
            var mine = await repository.ListMetadataOwnedAsync(owner);
            Assert.AreEqual(1, mine.Count);
            Assert.AreEqual(visible, mine[0].Id);
            Assert.AreEqual("ABCD", mine[0].Last4);
            var theirs = await repository.ListMetadataOwnedAsync(otherOwner);
            Assert.AreEqual(1, theirs.Count);
            Assert.AreEqual(foreign, theirs[0].Id);
            Assert.AreEqual(0, (await repository.ListMetadataOwnedAsync(Guid.NewGuid())).Count);
            Assert.AreEqual(3, audit.Reads);
            Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private sealed class QueryAudit : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Assert.IsFalse(command.CommandText.Contains("key_value_encrypted", StringComparison.OrdinalIgnoreCase));
                Reads++;
            }
            return ValueTask.FromResult(result);
        }
    }
}
