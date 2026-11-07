namespace APCS.Application.Abstractions.Commerce;

/// <summary>Reads public marketplace listings for trend research.</summary>
public interface ICommerceMarketDataConnector
{
    CommercePlatform Platform { get; }

    Task<CommerceMarketSearchResult> SearchPublicListingsAsync(
        CommerceMarketSearchRequest request,
        CancellationToken cancellationToken = default);
}
