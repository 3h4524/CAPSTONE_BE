using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Commerce;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class CommerceConnectorFactoryTests
{
    [TestMethod]
    public void Create_WhenPlatformIsRegistered_ReturnsMatchingConnector()
    {
        var connector = new StubCommerceConnector(CommercePlatform.Etsy);
        var factory = new CommerceConnectorFactory([connector]);

        var result = factory.Create(CommercePlatform.Etsy);

        result.Should().BeSameAs(connector);
    }

    [TestMethod]
    public void Create_WhenPlatformIsNotRegistered_ThrowsClearException()
    {
        var factory = new CommerceConnectorFactory([]);

        var act = () => factory.Create(CommercePlatform.Shopify);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Shopify*");
    }

    [TestMethod]
    public void Create_WhenPlatformHasDuplicateConnectors_ThrowsClearException()
    {
        var factory = new CommerceConnectorFactory([
            new StubCommerceConnector(CommercePlatform.Etsy),
            new StubCommerceConnector(CommercePlatform.Etsy)
        ]);

        var act = () => factory.Create(CommercePlatform.Etsy);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Multiple commerce connectors*");
    }

    private sealed class StubCommerceConnector(CommercePlatform platform) : ICommerceConnector
    {
        public CommercePlatform Platform { get; } = platform;

        public Task<IReadOnlyList<CommerceListing>> GetListingsAsync(
            CommerceConnection connection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CommerceListing>>([]);

        public Task<IReadOnlyList<CommercePerformanceSnapshot>> GetPerformanceAsync(
            CommerceConnection connection,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CommercePerformanceSnapshot>>([]);
    }
}
