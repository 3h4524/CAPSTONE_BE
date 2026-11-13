namespace APCS.Application.Abstractions.Commerce;

/// <summary>Market listing lookups that need the database's JSON operators.</summary>
public interface IMarketListingQueries
{
    /// <summary>The ids among <paramref name="opportunityIds"/> whose evidence includes a digital-download listing.</summary>
    Task<IReadOnlySet<Guid>> GetDigitalOpportunityIdsAsync(
        IReadOnlyCollection<Guid> opportunityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Slims up to <paramref name="batchSize"/> snapshots observed before <paramref name="observedBefore"/> that a newer
    /// snapshot of the same listing supersedes: their description goes and their raw payload keeps only the fields the
    /// radar reads (<see cref="Features.Commerce.MarketSnapshotRetention.KeptPayloadFields"/>). Figures, title and tags
    /// stay, so history and trends read the same. Returns how many were slimmed.
    /// </summary>
    Task<int> CompactSupersededSnapshotsAsync(DateTime observedBefore, int batchSize, CancellationToken cancellationToken = default);
}
