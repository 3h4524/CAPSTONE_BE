using System.Net;
using System.Text.Json;
using APCS.Application.Abstractions.Translation;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class GoogleTranslationProviderTests
{
    [TestMethod]
    public async Task TranslateAsync_ValidBatch_SendsTextsAndMapsReturnedTranslationsToHashes()
    {
        string? requestBody = null;
        Uri? requestUri = null;
        var handler = new StubHandler(async (request, ct) =>
        {
            requestUri = request.RequestUri;
            requestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"translations":[{"translatedText":"Xin chào"},{"translatedText":" "},{"translatedText":"Áo"}]}}""")
            };
        });
        var provider = CreateProvider(handler);
        TranslationInput[] inputs = [new("a", "Hello"), new("b", "Empty"), new("c", "Shirt")];

        var result = await provider.TranslateAsync(inputs, "en", "vi");

        result.Should().BeEquivalentTo(new Dictionary<string, string> { ["a"] = "Xin chào", ["c"] = "Áo" });
        requestUri!.Query.Should().Be("?key=test%20key");
        using var body = JsonDocument.Parse(requestBody!);
        body.RootElement.GetProperty("q").EnumerateArray().Select(x => x.GetString()).Should().Equal("Hello", "Empty", "Shirt");
        body.RootElement.GetProperty("source").GetString().Should().Be("en");
        body.RootElement.GetProperty("target").GetString().Should().Be("vi");
        body.RootElement.GetProperty("format").GetString().Should().Be("text");
    }

    [TestMethod]
    public async Task TranslateAsync_MissingConfigurationOrEmptyBatch_DoesNotSendARequest()
    {
        var handler = new StubHandler((_, _) => throw new AssertFailedException("No request expected."));
        var unconfigured = CreateProvider(handler, new TranslationOptions());
        var configured = CreateProvider(handler);

        (await unconfigured.TranslateAsync([new("a", "Hello")], "en", "vi")).Should().BeEmpty();
        (await configured.TranslateAsync([], "en", "vi")).Should().BeEmpty();
        handler.RequestCount.Should().Be(0);
    }

    [TestMethod]
    public async Task TranslateAsync_ProviderFails_ReturnsNoTranslations()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await CreateProvider(handler).TranslateAsync([new("a", "Hello")], "en", "vi");

        result.Should().BeEmpty();
        handler.RequestCount.Should().Be(1);
    }

    [TestMethod]
    public async Task TranslateAsync_InvalidJson_ReturnsNoTranslations()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") }));

        var result = await CreateProvider(handler).TranslateAsync([new("a", "Hello")], "en", "vi");

        result.Should().BeEmpty();
    }

    private static GoogleTranslationProvider CreateProvider(HttpMessageHandler handler, TranslationOptions? options = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? new TranslationOptions { ApiKey = "test key", Endpoint = "https://translation.example/v2" }), NullLogger<GoogleTranslationProvider>.Instance);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return send(request, cancellationToken);
        }
    }
}
