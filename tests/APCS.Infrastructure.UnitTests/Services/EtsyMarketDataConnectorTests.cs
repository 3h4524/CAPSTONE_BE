using System.Net;
using System.Net.Http.Json;
using System.Text;
using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class EtsyMarketDataConnectorTests
{
    [TestMethod]
    public async Task SearchPublicListingsAsync_MapsNumericIdsAndMoneyResponse()
    {
        var handler = new StubHandler("""
            {"count":1,"results":[{"listing_id":123,"user_id":456,"shop_id":789,"title":"Mountain Shirt","description":"A shirt","url":"https://etsy.com/listing/123","price":{"amount":2599,"divisor":100,"currency_code":"USD"},"quantity":4,"num_favorers":25,"views":1000,"original_creation_timestamp":1700000000,"last_modified_timestamp":1700000100,"tags":["hiking","gift"]}]}
            """);
        var factory = new StubHttpClientFactory(handler);
        var connector = new EtsyMarketDataConnector(factory, Microsoft.Extensions.Options.Options.Create(new EtsyOptions { ApiKey = "key", SharedSecret = "secret" }), NullLogger<EtsyMarketDataConnector>.Instance);

        var result = await connector.SearchPublicListingsAsync(new CommerceMarketSearchRequest(["mountain shirt"], 10));

        result.Should().ContainSingle();
        result[0].ExternalListingId.Should().Be("123");
        result[0].ExternalSellerId.Should().Be("456");
        result[0].Price.Should().Be(25.99m);
        result[0].Currency.Should().Be("USD");
        result[0].Favorites.Should().Be(25);
        handler.RequestUri.Should().Contain("keywords=mountain shirt");
    }

    [TestMethod]
    public async Task SearchPublicListingsAsync_RetriesTransientNetworkFailure()
    {
        var handler = new FlakyHandler();
        var factory = new StubHttpClientFactory(handler);
        var connector = new EtsyMarketDataConnector(
            factory,
            Microsoft.Extensions.Options.Options.Create(new EtsyOptions { ApiKey = "key" }),
            NullLogger<EtsyMarketDataConnector>.Instance);

        var result = await connector.SearchPublicListingsAsync(new CommerceMarketSearchRequest(["hiking shirt"], 10));

        result.Should().ContainSingle();
        handler.CallCount.Should().Be(2);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://api.etsy.com/v3/application/") };
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FlakyHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
                throw new HttpRequestException("No such host is known.");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"count\":1,\"results\":[{\"listing_id\":123,\"title\":\"Hiking Shirt\"}]}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
