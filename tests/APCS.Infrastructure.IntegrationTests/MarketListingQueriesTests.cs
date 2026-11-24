using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class MarketListingQueriesTests(TestContext testContext)
{
    [TestMethod]
    [DataRow(DateTimeKind.Unspecified)]
    [DataRow(DateTimeKind.Utc)]
    public async Task CompactSupersededSnapshotsAsync_PostgreSql_SlimsOnlySupersededRowsBeforeTheCutoff(DateTimeKind kind)
    {
        var connectionString = Environment.GetEnvironmentVariable("APCS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Set APCS_TEST_CONNECTION_STRING to run the opt-in PostgreSQL test.");
            return;
        }

        await TestSchema.EnsureReadyAsync(connectionString);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString, o => o.UseVector()).Options;
        await using var db = new AppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(testContext.CancellationToken);
        try
        {
            // Session-local table shadows the unqualified mapping. No public rows are written.
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TEMP TABLE market_listing_snapshots (LIKE public.market_listing_snapshots INCLUDING ALL) ON COMMIT DROP",
                testContext.CancellationToken);
            // The column holds UTC without a zone, so the session's zone must not move the cutoff.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL TIME ZONE 'Asia/Ho_Chi_Minh'", testContext.CancellationToken);

            var cutoff = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Unspecified);
            var superseded = Snapshot("superseded", cutoff.AddSeconds(-1));
            var atCutoff = Snapshot("at-cutoff", cutoff);
            var afterCutoff = Snapshot("after-cutoff", cutoff.AddHours(1));
            var latest = Snapshot("latest", cutoff.AddDays(-2));
            db.MarketListingSnapshots.AddRange(
                superseded, Snapshot("superseded", cutoff.AddDays(2)),
                atCutoff, Snapshot("at-cutoff", cutoff.AddDays(2)),
                afterCutoff, Snapshot("after-cutoff", cutoff.AddDays(2)),
                latest);
            await db.SaveChangesAsync(testContext.CancellationToken);

            var changed = await new MarketListingQueries(db)
                .CompactSupersededSnapshotsAsync(DateTime.SpecifyKind(cutoff, kind), 100, testContext.CancellationToken);

            db.ChangeTracker.Clear();
            var descriptions = await db.MarketListingSnapshots.ToDictionaryAsync(row => row.Id, row => row.Description,
                testContext.CancellationToken);
            Assert.AreEqual(1, changed);
            Assert.IsNull(descriptions[superseded.Id]);
            Assert.IsNotNull(descriptions[atCutoff.Id]);
            Assert.IsNotNull(descriptions[afterCutoff.Id]);
            Assert.IsNotNull(descriptions[latest.Id]);
        }
        finally
        {
            await transaction.RollbackAsync(testContext.CancellationToken);
        }
    }

    private static MarketListingSnapshot Snapshot(string listingId, DateTime observedAt) => new()
    {
        Id = Guid.NewGuid(),
        Platform = "etsy",
        ExternalListingId = listingId,
        Title = listingId,
        Description = "Full listing text",
        Tags = "[]",
        RawPayload = """{"title":"Full listing title","listing_type":"physical"}""",
        ObservedAt = observedAt,
        CollectedAt = observedAt
    };
}
