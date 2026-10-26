namespace APCS.Application.Abstractions.Commerce;

/// <summary>
/// Resolves the connector for a connected commerce platform.
/// </summary>
public interface ICommerceConnectorFactory
{
    ICommerceConnector Create(CommercePlatform platform);
}
