using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

public sealed class CommerceMarketDataConnectorFactory(IEnumerable<ICommerceMarketDataConnector> connectors)
    : ICommerceMarketDataConnectorFactory
{
    private readonly IReadOnlyList<ICommerceMarketDataConnector> _connectors = connectors.ToArray();

    public ICommerceMarketDataConnector Create(CommercePlatform platform)
    {
        var matches = _connectors.Where(item => item.Platform == platform).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No public market connector is registered for platform '{platform}'."),
            _ => throw new InvalidOperationException($"Multiple public market connectors are registered for platform '{platform}'.")
        };
    }
}
