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

    [TestMethod]
    public async Task GenerateAsync_WithASchemaAndModel_SendsThemInsteadOfTheConfiguredOnes()
    {
        string? path = null, body = null;
        var provider = Provider(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Response(HttpStatusCode.OK, Generated);
        });

        await provider.GenerateAsync(new GeminiTextGenerationRequest(
            "Review the plans.", ResponseSchema: new { type = "OBJECT" }, Model: "review-model", Temperature: 0.2m));

        path.Should().Be("/v1beta/models/review-model:generateContent");
        body.Should().Contain("\"responseSchema\":{\"type\":\"OBJECT\"}").And.Contain("\"temperature\":0.2");
    }

    [TestMethod]
    public async Task GenerateAsync_WithoutASchema_SendsNoResponseSchema()
    {
        string? body = null;
        var provider = Provider(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Response(HttpStatusCode.OK, Generated);
        });

        await provider.GenerateAsync(new GeminiTextGenerationRequest("Write a listing."));

        body.Should().NotContain("responseSchema");
    }

    [TestMethod]
    [DataRow("""{"promptFeedback":{"blockReason":"PROHIBITED_CONTENT"}}""", "PROHIBITED_CONTENT")]
    [DataRow("""{"candidates":[{"finishReason":"SAFETY"}]}""", "SAFETY")]
    public async Task GenerateAsync_RefusedBySafetyFilter_SaysSoInsteadOfAnEmptyAnswer(string body, string reason)
    {
        var provider = Provider(_ => Response(HttpStatusCode.OK, body));

        var action = () => provider.GenerateAsync(new GeminiTextGenerationRequest("Classify this."));

        (await action.Should().ThrowAsync<GeminiContentBlockedException>()).Which.Reason.Should().Be(reason);
    }

    [TestMethod]
    public async Task GenerateAsync_EmptyAnswerWithoutABlock_IsStillAnUnusableAnswer()
    {
        var provider = Provider(_ => Response(HttpStatusCode.OK, """{"candidates":[{"finishReason":"STOP"}]}"""));

        var action = () => provider.GenerateAsync(new GeminiTextGenerationRequest("Classify this."));

        (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<GeminiContentBlockedException>();
    }

    [TestMethod]
    public async Task GenerateStreamingAsync_HandsEachChunkOverAndReturnsTheWholeAnswerWithTheLastChunksUsage()
    {
        string? path = null, body = null;
        var observer = new RecordingStreamObserver();
        var provider = Provider(request =>
        {
            path = request.RequestUri!.PathAndQuery;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Stream(
                """{"candidates":[{"content":{"parts":[{"text":"{\"title\":"}]}}],"responseId":"response-1"}""",
                """{"candidates":[{"content":{"parts":[{"text":"thinking","thought":true},{"text":"\"ok\"}"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":5},"responseId":"response-1"}""");
        });

        var result = await provider.GenerateStreamingAsync(new GeminiTextGenerationRequest("Write a listing.", ResponseSchema: new { type = "OBJECT" }), observer);

        path.Should().Be("/v1beta/models/primary-model:streamGenerateContent?alt=sse");
        body.Should().Contain("\"responseMimeType\":\"application/json\"").And.Contain("\"responseSchema\":{\"type\":\"OBJECT\"}");
        observer.Events.Should().Equal("attempt:primary-model", "text:{\"title\":", "text:\"ok\"}");
        result.Should().Be(new GeminiTextGenerationResult("{\"title\":\"ok\"}", "primary-model", 12, 5, "response-1"));
    }

    [TestMethod]
    public async Task GenerateStreamingAsync_PrimaryModelOverloaded_SignalsARestartAndUsesTheFallbackModel()
    {
        var observer = new RecordingStreamObserver();
        var provider = Provider(request => request.RequestUri!.AbsolutePath.Contains("primary-model")
            ? Response(HttpStatusCode.ServiceUnavailable, Overloaded)
            : Stream("""{"candidates":[{"content":{"parts":[{"text":"{\"title\":\"ok\"}"}]}}]}"""));

        var result = await provider.GenerateStreamingAsync(new GeminiTextGenerationRequest("Write a listing."), observer);

        result.Model.Should().Be("fallback-model");
        observer.Events.Should().Equal("attempt:primary-model", "restart:fallback-model", "text:{\"title\":\"ok\"}");
    }

    [TestMethod]
    public async Task GenerateStreamingAsync_OverloadedInTheMiddleOfTheStream_StartsAgainOnTheFallbackModel()
    {
        var observer = new RecordingStreamObserver();
        var provider = Provider(request => request.RequestUri!.AbsolutePath.Contains("primary-model")
            ? Stream("""{"candidates":[{"content":{"parts":[{"text":"{\"ti"}]}}]}""", """{"error":{"code":503,"message":"Overloaded."}}""")
            : Stream("""{"candidates":[{"content":{"parts":[{"text":"{\"title\":\"ok\"}"}]}}]}"""));

        var result = await provider.GenerateStreamingAsync(new GeminiTextGenerationRequest("Write a listing."), observer);

        result.Text.Should().Be("{\"title\":\"ok\"}");
        observer.Events.Should().Equal("attempt:primary-model", "text:{\"ti", "restart:fallback-model", "text:{\"title\":\"ok\"}");
    }

    [TestMethod]
    public async Task GenerateStreamingAsync_RequestRejected_IsNotRetried()
    {
        var calls = 0;
        var provider = Provider(_ => { calls++; return Response(HttpStatusCode.BadRequest, """{"error":{"message":"API key not valid."}}"""); });

        var act = () => provider.GenerateStreamingAsync(new GeminiTextGenerationRequest("Write a listing."), new RecordingStreamObserver());

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        calls.Should().Be(1);
    }

    [TestMethod]
    public async Task GenerateStreamingAsync_RefusedBySafetyFilter_SaysSo()
    {
        var provider = Provider(_ => Stream("""{"candidates":[{"finishReason":"SAFETY"}]}"""));

        var act = () => provider.GenerateStreamingAsync(new GeminiTextGenerationRequest("Write a listing."), new RecordingStreamObserver());

        (await act.Should().ThrowAsync<GeminiContentBlockedException>()).Which.Reason.Should().Be("SAFETY");
    }

    [TestMethod]
    public async Task GenerateStreamingAsync_EmptyAnswer_IsAnUnusableAnswer()
    {
        var provider = Provider(_ => Stream("""{"candidates":[{"finishReason":"STOP"}]}"""));

        var act = () => provider.GenerateStreamingAsync(new GeminiTextGenerationRequest("Write a listing."), new RecordingStreamObserver());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<GeminiContentBlockedException>();
    }

    [TestMethod]
    public async Task GenerateStreamingAsync_NoModelAnswersInTime_FailsAsATimeout()
    {
        var provider = new GeminiTextGenerationProvider(
            new HttpClient(new HangingHandler()) { BaseAddress = new Uri("https://example.test/v1beta/") },
            Microsoft.Extensions.Options.Options.Create(new GeminiTextGenerationOptions { ApiKey = "key", Model = "primary-model", FallbackModel = "" }));

        var act = () => provider.GenerateStreamingAsync(
            new GeminiTextGenerationRequest("Write a listing.", AttemptTimeout: TimeSpan.FromMilliseconds(20)), new RecordingStreamObserver());

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    private static HttpResponseMessage Stream(params string[] chunks) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(chunks.Select(chunk => $"data: {chunk}\r\n\r\n")), Encoding.UTF8, "text/event-stream")
        };

    private sealed class RecordingStreamObserver : IGeminiTextStreamObserver
    {
        public List<string> Events { get; } = [];

        public Task AttemptStartedAsync(string model, bool restarted, CancellationToken cancellationToken)
        {
            Events.Add($"{(restarted ? "restart" : "attempt")}:{model}");
            return Task.CompletedTask;
        }

        public Task TextReceivedAsync(string text, CancellationToken cancellationToken)
        {
            Events.Add($"text:{text}");
            return Task.CompletedTask;
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
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
