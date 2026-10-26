namespace APCS.Application.Abstractions.Commerce;

public interface ICommerceMarketDataConnectorFactory
{
    ICommerceMarketDataConnector Create(CommercePlatform platform);
}
