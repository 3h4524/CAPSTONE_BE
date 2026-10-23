using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Resolves registered commerce connectors without exposing platform-specific types
/// to application use cases.
/// </summary>
public sealed class CommerceConnectorFactory(IEnumerable<ICommerceConnector> connectors)
    : ICommerceConnectorFactory
{
    private readonly IReadOnlyList<ICommerceConnector> _connectors = connectors.ToArray();

    public ICommerceConnector Create(CommercePlatform platform)
    {
        var matches = _connectors
            .Where(connector => connector.Platform == platform)
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No commerce connector is registered for platform '{platform}'."),
            _ => throw new InvalidOperationException(
                $"Multiple commerce connectors are registered for platform '{platform}'.")
        };
    }
}
