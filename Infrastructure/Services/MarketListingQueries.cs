using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Commerce;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Services;

public sealed class MarketListingQueries(AppDbContext dbContext) : IMarketListingQueries
{
    public async Task<IReadOnlySet<Guid>> GetDigitalOpportunityIdsAsync(
        IReadOnlyCollection<Guid> opportunityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(opportunityIds);
        if (opportunityIds.Count == 0)
            return new HashSet<Guid>();

        var ids = opportunityIds.Distinct().ToArray();
        var digital = await dbContext.MarketOpportunityEvidences
            .AsNoTracking()
            .Where(item => ids.Contains(item.OpportunityId)
                && item.Snapshot.RawPayload != null
                && EF.Functions.JsonContains(item.Snapshot.RawPayload, MarketListingKind.DigitalListingJson))
            .Select(item => item.OpportunityId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return digital.ToHashSet();
    }

    /// <inheritdoc />
    public Task<int> CompactSupersededSnapshotsAsync(DateTime observedBefore, int batchSize, CancellationToken cancellationToken = default)
    {
        // A slimmed payload has no "title", so a snapshot is slimmed once. The fields kept are
        // MarketSnapshotRetention.KeptPayloadFields; jsonb_strip_nulls drops the ones a listing never had.
        var cutoff = DateTime.SpecifyKind(observedBefore, DateTimeKind.Unspecified);
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE market_listing_snapshots s
            SET raw_payload = jsonb_strip_nulls(jsonb_build_object(
                    'listing_type', s.raw_payload -> 'listing_type',
                    'original_creation_timestamp', s.raw_payload -> 'original_creation_timestamp',
                    'is_personalizable', s.raw_payload -> 'is_personalizable',
                    'taxonomy_id', s.raw_payload -> 'taxonomy_id')),
                description = NULL
            WHERE s.id IN (
                SELECT old.id FROM market_listing_snapshots old
                WHERE old.observed_at < {cutoff} AND NOT old.is_synthetic
                  AND (old.raw_payload ? 'title' OR old.description IS NOT NULL)
                  AND EXISTS (SELECT 1 FROM market_listing_snapshots newer
                              WHERE newer.platform = old.platform AND newer.external_listing_id = old.external_listing_id
                                AND newer.observed_at > old.observed_at)
                LIMIT {batchSize})
            """, cancellationToken);
    }
}
