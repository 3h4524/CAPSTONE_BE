using System.Net;
using System.Text;
using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class GeminiTextGenerationProviderTests
{
    private const string Generated = """{"candidates":[{"content":{"parts":[{"text":"{\"title\":\"ok\"}"}]}}]}""";
    private const string Overloaded = """{"error":{"message":"This model is currently experiencing high demand."}}""";

    [TestMethod]
    public async Task GenerateAsync_PrimaryModelOverloaded_UsesTheFallbackModel()
    {
        var called = new List<string>();
        var provider = Provider(request =>
        {
            called.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.Contains("primary-model") ? Response(HttpStatusCode.ServiceUnavailable, Overloaded) : Response(HttpStatusCode.OK, Generated);
        });

        var result = await provider.GenerateAsync(new GeminiTextGenerationRequest("Write a listing."));

        result.Model.Should().Be("fallback-model");
        result.Text.Should().Contain("title");
        called.Should().Equal("/v1beta/models/primary-model:generateContent", "/v1beta/models/fallback-model:generateContent");
    }

    [TestMethod]
    public async Task GenerateAsync_PrimaryModelAnswers_DoesNotTouchTheFallbackModel()
    {
        var calls = 0;
        var provider = Provider(_ => { calls++; return Response(HttpStatusCode.OK, Generated); });

        var result = await provider.GenerateAsync(new GeminiTextGenerationRequest("Write a listing."));

        result.Model.Should().Be("primary-model");
        calls.Should().Be(1);
    }

    [TestMethod]
    public async Task GenerateAsync_RequestRejected_IsNotRetried()
    {
        var calls = 0;
        var provider = Provider(_ => { calls++; return Response(HttpStatusCode.BadRequest, """{"error":{"message":"API key not valid."}}"""); });

        var act = () => provider.GenerateAsync(new GeminiTextGenerationRequest("Write a listing."));

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        calls.Should().Be(1);
    }

    private static GeminiTextGenerationProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new Handler(send)) { BaseAddress = new Uri("https://example.test/v1beta/") },
            Microsoft.Extensions.Options.Options.Create(new GeminiTextGenerationOptions { ApiKey = "key", Model = "primary-model", FallbackModel = "fallback-model" }));

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
