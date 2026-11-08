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
}
