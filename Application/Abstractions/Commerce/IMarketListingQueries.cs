namespace APCS.Application.Abstractions.Commerce;

/// <summary>Market listing lookups that need the database's JSON operators.</summary>
public interface IMarketListingQueries
{
    /// <summary>The ids among <paramref name="opportunityIds"/> whose evidence includes a digital-download listing.</summary>
    Task<IReadOnlySet<Guid>> GetDigitalOpportunityIdsAsync(
        IReadOnlyCollection<Guid> opportunityIds,
        CancellationToken cancellationToken = default);
}
