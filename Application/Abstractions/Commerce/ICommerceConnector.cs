namespace APCS.Application.Abstractions.Commerce;

/// <summary>
/// Application contract for reading data from one commerce platform.
/// Platform-specific API models stay inside the Infrastructure adapter.
/// </summary>
public interface ICommerceConnector
{
    CommercePlatform Platform { get; }

    Task<IReadOnlyList<CommerceListing>> GetListingsAsync(
        CommerceConnection connection,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommercePerformanceSnapshot>> GetPerformanceAsync(
        CommerceConnection connection,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}
