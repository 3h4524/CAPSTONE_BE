using System.Net;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class GeminiImageProviderTests
{
    private static readonly byte[] Image = [9, 8, 7, 6];
    private static readonly string Generated =
        """{"candidates":[{"content":{"parts":[{"text":"Here it is."},{"inlineData":{"mimeType":"image/jpeg","data":"IMAGE"}}]},"finishReason":"STOP"}]}"""
            .Replace("IMAGE", Convert.ToBase64String(Image));

    private HttpRequestMessage? sent;
    private string body = "";

    [TestMethod]
    public async Task GenerateAsync_ImageReturned_GivesItsBytesModelAndCost()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, Generated));

        var result = await provider.GenerateAsync("key", "A fox on a bicycle.", "1:1");

        result.IsSuccess.Should().BeTrue();
        result.ImageBytes.Should().Equal(Image);
        result.MimeType.Should().Be("image/jpeg");
        result.ModelUsed.Should().Be("image-model");
        result.EstimatedCostUsd.Should().Be(0.05m);
        sent!.RequestUri!.AbsoluteUri.Should().EndWith("models/image-model:generateContent");
        sent.Headers.GetValues("x-goog-api-key").Should().Equal("key");
        using var request = JsonDocument.Parse(body);
        var part = request.RootElement.GetProperty("contents")[0].GetProperty("parts")[0];
        part.GetProperty("text").GetString().Should().Be("A fox on a bicycle.\nImage aspect ratio: 1:1.");
        // A text part must not carry an empty image next to it.
        part.TryGetProperty("inlineData", out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task GenerateAsync_NoAspectRatio_SendsThePromptUnchanged()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, Generated));

        await provider.GenerateAsync("key", "A fox on a bicycle.", " ");

        using var request = JsonDocument.Parse(body);
        request.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("A fox on a bicycle.");
    }

    [TestMethod]
    public async Task ComposeAsync_SendsTheImagesInOrderBeforeThePrompt()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, Generated));

        var result = await provider.ComposeAsync("key", "Dress the person in the product.",
            [new ImageReference([1, 2], "image/jpeg"), new ImageReference([3, 4], "image/png")]);

        result.ImageBytes.Should().Equal(Image);
        using var request = JsonDocument.Parse(body);
        var parts = request.RootElement.GetProperty("contents")[0].GetProperty("parts");
        parts.GetArrayLength().Should().Be(3);
        parts[0].GetProperty("inlineData").GetProperty("mimeType").GetString().Should().Be("image/jpeg");
        parts[0].GetProperty("inlineData").GetProperty("data").GetString().Should().Be(Convert.ToBase64String([1, 2]));
        parts[0].TryGetProperty("text", out _).Should().BeFalse();
        parts[1].GetProperty("inlineData").GetProperty("mimeType").GetString().Should().Be("image/png");
        parts[2].GetProperty("text").GetString().Should().Be("Dress the person in the product.");
    }

    [TestMethod]
    public async Task GenerateAsync_GoogleRefuses_FailsWithItsAnswer()
    {
        var provider = Provider(_ => Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"Quota exceeded."}}"""));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.IsBlocked.Should().BeFalse();
        result.ErrorMessage.Should().Contain("429").And.Contain("Quota exceeded.");
    }

    [TestMethod]
    public async Task GenerateAsync_GoogleBusyOnce_SendsTheCallAgainAndGetsTheImage()
    {
        var requests = 0;
        var provider = Provider(_ => ++requests == 1
            ? Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"Quota exceeded.","details":[{"retryDelay":"0s"}]}}""")
            : Json(HttpStatusCode.OK, Generated));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        requests.Should().Be(2);
        using var request = JsonDocument.Parse(body);
        request.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()
            .Should().Be("A fox.\nImage aspect ratio: 1:1.", "the repeat sends the same request");
    }

    [TestMethod]
    public async Task GenerateAsync_GoogleInternalErrorOnce_SendsTheCallAgainAndGetsTheImage()
    {
        var requests = 0;
        var provider = Provider(_ => ++requests == 1
            ? Json(HttpStatusCode.InternalServerError, """{"error":{"code":500,"message":"Internal error encountered.","status":"INTERNAL"}}""")
            : Json(HttpStatusCode.OK, Generated));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        requests.Should().Be(2);
    }

    [TestMethod]
    public async Task GenerateAsync_GoogleStaysBusy_GivesUpAfterTheRetries()
    {
        var requests = 0;
        var provider = Provider(_ =>
        {
            requests++;
            return Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"The model is overloaded."}}""");
        });

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("503").And.Contain("overloaded");
        requests.Should().Be(3, "the first call and two retries");
    }

    [TestMethod]
    public async Task GenerateAsync_GoogleRejectsTheRequest_IsNotSentAgain()
    {
        var requests = 0;
        var provider = Provider(_ =>
        {
            requests++;
            return Json(HttpStatusCode.BadRequest, """{"error":{"message":"API key not valid."}}""");
        });

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.ErrorMessage.Should().Contain("400");
        requests.Should().Be(1);
    }

    [TestMethod]
    public async Task GenerateAsync_PromptBlocked_IsReportedAsBlocked()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}"""));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsBlocked.Should().BeTrue();
        result.ErrorMessage.Should().Contain("SAFETY");
    }

    [TestMethod]
    public async Task GenerateAsync_GenerationStoppedBySafety_IsReportedAsBlocked()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"No."}]},"finishReason":"IMAGE_SAFETY"}]}"""));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsBlocked.Should().BeTrue();
        result.ErrorMessage.Should().Contain("IMAGE_SAFETY");
    }

    [TestMethod]
    [DataRow("""{"candidates":[{"content":{"parts":[{"text":"Only words."}]},"finishReason":"STOP"}]}""", "did not return an image")]
    [DataRow("""{"candidates":[]}""", "no candidates")]
    [DataRow("this is not json", "unrecognized response")]
    public async Task GenerateAsync_NoImageInTheAnswer_Fails(string response, string message)
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, response));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.IsBlocked.Should().BeFalse();
        result.ErrorMessage.Should().Contain(message);
    }

    [TestMethod]
    public async Task GenerateAsync_GoogleUnreachable_FailsWithoutThrowing()
    {
        var provider = Provider(_ => throw new HttpRequestException("No route to host."));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Could not reach Gemini");
    }

    [TestMethod]
    public async Task GenerateAsync_RequestTimesOut_FailsWithoutThrowing()
    {
        var provider = Provider(_ => throw new TaskCanceledException());

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    private GeminiImageProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(x => x.CreateClient("Gemini")).Returns(() => new HttpClient(new Handler(async request =>
        {
            sent = request;
            body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            return send(request);
        })));
        return new(clients.Object, Microsoft.Extensions.Options.Options.Create(new GeminiOptions
        {
            DefaultModel = "image-model", EstimatedCostPerImageUsd = 0.05m, MaxRetries = 2, RetryBaseDelay = TimeSpan.Zero
        }));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
