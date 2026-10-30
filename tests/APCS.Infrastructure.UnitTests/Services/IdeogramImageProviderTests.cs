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
public sealed class IdeogramImageProviderTests
{
    private const string ImageUrl = "https://ideogram.ai/api/images/ephemeral/abc.png?exp=1";
    private const string FreshImageUrl = "https://ideogram.ai/api/images/ephemeral/abc.png?exp=2";
    private const string GenerationUrl = "https://api.ideogram.ai/v2/generations/g1";
    private static readonly byte[] Image = [9, 8, 7, 6];
    private const string Accepted = """{"generation_id":"g1","seed":7}""";
    private const string Pending = """{"generation_id":"g1","status":"pending","created":"2026-10-21T00:00:00Z"}""";
    private static readonly string Completed = CompletedWith(ImageUrl);

    private readonly List<HttpRequestMessage> sent = [];
    private string body = "";

    [TestMethod]
    public async Task GenerateAsync_Transparent_AsksForTheTransparentModelAtTwoK_PollsAndDownloadsTheImage()
    {
        var provider = Provider(Answer);

        var result = await provider.GenerateAsync("key", "A fox on a bicycle.", "16:9", transparentBackground: true);

        result.IsSuccess.Should().BeTrue();
        result.ImageBytes.Should().Equal(Image);
        result.MimeType.Should().Be("image/png");
        result.ModelUsed.Should().Be("ideogram-4-transparent");
        sent[0].Method.Should().Be(HttpMethod.Post);
        sent[0].RequestUri!.AbsoluteUri.Should().Be("https://api.ideogram.ai/v2/image/generate/ideogram-4-transparent");
        sent[0].Headers.GetValues("Api-Key").Should().Equal("key");
        using var request = JsonDocument.Parse(body);
        request.RootElement.GetProperty("prompt").GetString().Should().Be("A fox on a bicycle.");
        request.RootElement.GetProperty("rendering_speed").GetString().Should().Be("turbo");
        request.RootElement.GetProperty("magic_prompt").GetString().Should().Be("off");
        request.RootElement.GetProperty("output_resolution").GetString().Should().Be("2k");
        request.RootElement.GetProperty("aspect_ratio").GetString().Should().Be("16x9");
        request.RootElement.GetProperty("num_images").GetInt32().Should().Be(1);
        request.RootElement.GetProperty("async").GetBoolean().Should().BeTrue();
        request.RootElement.GetProperty("enable_copyright_detection").GetBoolean().Should().BeTrue();
        sent[1].RequestUri!.AbsoluteUri.Should().Be(GenerationUrl);
        sent[1].Headers.GetValues("Api-Key").Should().Equal("key");
        // The key goes to the API only, never to the image link.
        sent[2].RequestUri!.AbsoluteUri.Should().Be(ImageUrl);
        sent[2].Headers.Contains("Api-Key").Should().BeFalse();
    }

    [TestMethod]
    public async Task GenerateAsync_PromptToFillOut_TurnsIdeogramsMagicPromptOn()
    {
        var provider = Provider(Answer);

        await provider.GenerateAsync("key", "Game Day.", "1:1", transparentBackground: true, enrichPrompt: true);

        using var request = JsonDocument.Parse(body);
        request.RootElement.GetProperty("magic_prompt").GetString().Should().Be("on");
    }

    [TestMethod]
    public async Task GenerateAsync_ReportsWhatIdeogramCharged_HowLongEachPartTook_AndWhatItDrewFrom()
    {
        var provider = Provider(Answer);

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.EstimatedCostUsd.Should().Be(0.03m, "Ideogram reported 30,000 micro-dollars");
        result.Seed.Should().Be(7);
        result.ProviderPrompt.Should().Be("{\"high_level_description\":\"A fox.\"}");
        result.GenerateTime.Should().NotBeNull();
        result.DownloadTime.Should().NotBeNull();
    }

    [TestMethod]
    public async Task GenerateAsync_WithoutAReportedCharge_UsesThePriceOfTheRenderingSpeed()
    {
        var provider = Provider(request => request.RequestUri!.AbsoluteUri == GenerationUrl
            ? Json(HttpStatusCode.OK, Completed.Replace("\"usage_cost_usd_micros\":30000,", ""))
            : Answer(request), new IdeogramOptions { RenderingSpeed = "quality" });

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.EstimatedCostUsd.Should().Be(0.10m);
        provider.EstimatedCostPerImageUsd.Should().Be(0.10m);
    }

    [TestMethod]
    public async Task GenerateAsync_StillBeingMade_AsksAgainUntilItIsDone()
    {
        var polls = 0;
        var provider = Provider(request => request.RequestUri!.AbsoluteUri == GenerationUrl
            ? Json(HttpStatusCode.OK, ++polls < 3 ? Pending : Completed)
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        polls.Should().Be(3);
    }

    [TestMethod]
    public async Task GenerateAsync_BusyWhileAskingAboutTheImage_WaitsItOut()
    {
        var polls = 0;
        var provider = Provider(request => request.RequestUri!.AbsoluteUri == GenerationUrl
            ? ++polls == 1 ? Json(HttpStatusCode.TooManyRequests, """{"error":"Slow down."}""") : Json(HttpStatusCode.OK, Completed)
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        polls.Should().Be(2);
    }

    [TestMethod]
    public async Task GenerateAsync_GenerationFailsTheContentPolicy_IsBlocked()
    {
        var provider = Provider(request => request.RequestUri!.AbsoluteUri == GenerationUrl
            ? Json(HttpStatusCode.OK, """{"generation_id":"g1","status":"failed","created":"2026-10-21T00:00:00Z","failure_reason":"content_policy_violation"}""")
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsBlocked.Should().BeTrue();
        result.ErrorMessage.Should().Contain("content_policy_violation");
    }

    [TestMethod]
    public async Task GenerateAsync_GenerationFailsOtherwise_Fails()
    {
        var provider = Provider(request => request.RequestUri!.AbsoluteUri == GenerationUrl
            ? Json(HttpStatusCode.OK, """{"generation_id":"g1","status":"failed","created":"2026-10-21T00:00:00Z","failure_reason":"internal_error"}""")
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.IsBlocked.Should().BeFalse();
        result.ErrorMessage.Should().Be("Ideogram could not make the image: internal_error");
    }

    [TestMethod]
    public async Task GenerateAsync_WithABackdrop_CallsTheModelWithTheTwoKSizeOfTheRatio()
    {
        var provider = Provider(Answer);

        var result = await provider.GenerateAsync("key", "A poster.", "3:4");

        result.ModelUsed.Should().Be("ideogram-4");
        sent[0].RequestUri!.AbsoluteUri.Should().Be("https://api.ideogram.ai/v2/image/generate/ideogram-4");
        using var request = JsonDocument.Parse(body);
        request.RootElement.GetProperty("resolution").GetString().Should().Be("1728x2304");
        request.RootElement.TryGetProperty("aspect_ratio", out _).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(false, "resolution", "1280x720")]
    [DataRow(true, "output_resolution", "1k")]
    public async Task GenerateAsync_AtOneK_AsksForTheOneKSize(bool transparent, string property, string expected)
    {
        var provider = Provider(Answer, new IdeogramOptions { OutputResolution = "1k" });

        await provider.GenerateAsync("key", "A poster.", "16:9", transparent);

        using var request = JsonDocument.Parse(body);
        request.RootElement.GetProperty(property).GetString().Should().Be(expected);
    }

    [TestMethod]
    public async Task GenerateAsync_ImageFailsTheSafetyCheck_IsBlocked()
    {
        var provider = Provider(request => request.RequestUri!.AbsoluteUri == GenerationUrl
            ? Json(HttpStatusCode.OK,
                """{"generation_id":"g1","status":"completed","created":"2026-10-21T00:00:00Z","data":[{"prompt":"p","resolution":"2048x2048","is_image_safe":false,"seed":7,"url":null}]}""")
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1", transparentBackground: true);

        result.IsBlocked.Should().BeTrue();
        result.ErrorMessage.Should().Contain("copyright");
        sent.Should().HaveCount(2, "nothing is downloaded");
    }

    [TestMethod]
    public async Task GenerateAsync_PromptFailsTheSafetyCheck_IsBlocked()
    {
        var provider = Provider(_ => Json(HttpStatusCode.UnprocessableEntity, """{"error":"Prompt failed the safety check."}"""));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsBlocked.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Prompt failed the safety check.");
    }

    [TestMethod]
    public async Task GenerateAsync_NoCredits_StopsTheJobWithIdeogramsAnswerWithoutRetrying()
    {
        var provider = Provider(_ => Json(HttpStatusCode.PaymentRequired,
            """{"error":"Add a payment method and credits to use this API key.","reject_reason":"insufficient_funds"}"""));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.IsBlocked.Should().BeFalse();
        result.StopsJob.Should().BeTrue();
        result.ErrorMessage.Should().Be("Ideogram returned 402: Add a payment method and credits to use this API key.");
        sent.Should().ContainSingle();
    }

    [TestMethod]
    public async Task GenerateAsync_KeyNoLongerAccepted_StopsTheJob()
    {
        var provider = Provider(_ => Json(HttpStatusCode.Unauthorized, """{"error":"Invalid API key."}"""));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.StopsJob.Should().BeTrue();
    }

    [TestMethod]
    public async Task GenerateAsync_TooManyImagesAtOnce_TriesAgainAndSucceeds()
    {
        var calls = 0;
        var provider = Provider(request => request.Method == HttpMethod.Post && ++calls == 1
            ? Json(HttpStatusCode.TooManyRequests, """{"error":"Too many requests.","reject_reason":"inflight_limit"}""")
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        calls.Should().Be(2);
    }

    [TestMethod]
    public async Task GenerateAsync_InternalErrorOnce_TriesAgainAndSucceeds()
    {
        var calls = 0;
        var provider = Provider(request => request.Method == HttpMethod.Post && ++calls == 1
            ? Json(HttpStatusCode.InternalServerError, """{"error":"Internal server error."}""")
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        calls.Should().Be(2);
    }

    [TestMethod]
    public async Task GenerateAsync_DailyLimitReached_IsNotRetried_AndStopsTheJob()
    {
        var provider = Provider(_ => Json(HttpStatusCode.TooManyRequests, """{"error":"Daily limit reached.","reject_reason":"daily_limit"}"""));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.ErrorMessage.Should().Contain("Daily limit reached.");
        result.StopsJob.Should().BeTrue();
        sent.Should().ContainSingle();
    }

    [TestMethod]
    public async Task GenerateAsync_LinkFailsOnce_DownloadsAgainFromAFreshLink()
    {
        var polls = 0;
        var provider = Provider(request => request.RequestUri!.AbsoluteUri switch
        {
            GenerationUrl => Json(HttpStatusCode.OK, ++polls == 1 ? Completed : CompletedWith(FreshImageUrl)),
            ImageUrl => new HttpResponseMessage(HttpStatusCode.Forbidden),
            _ => Answer(request)
        });

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        result.ImageBytes.Should().Equal(Image);
        sent.Select(x => x.RequestUri!.AbsoluteUri).Should().ContainInOrder(ImageUrl, GenerationUrl, FreshImageUrl);
    }

    [TestMethod]
    public async Task GenerateAsync_ImageLinkNeverDownloads_FailsAfterEveryAttempt()
    {
        var provider = Provider(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == ImageUrl
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("403");
        sent.Count(x => x.RequestUri!.AbsoluteUri == ImageUrl).Should().Be(3);
    }

    [TestMethod]
    public async Task GenerateAsync_AnsweredStraightAway_NeedsNoPolling()
    {
        var provider = Provider(request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, """{"generation_id":"g1","seed":7,"data":[{"prompt":"p","resolution":"2048x2048","is_image_safe":true,"seed":7,"url":"https://ideogram.ai/api/images/ephemeral/abc.png?exp=1"}]}""")
            : Answer(request));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.IsSuccess.Should().BeTrue();
        sent.Should().NotContain(x => x.RequestUri!.AbsoluteUri == GenerationUrl);
    }

    [TestMethod]
    public async Task GenerateAsync_IdeogramUnreachable_Fails()
    {
        var provider = Provider(_ => throw new HttpRequestException("No route to host."));

        var result = await provider.GenerateAsync("key", "A fox.", "1:1");

        result.ErrorMessage.Should().Be("Could not reach Ideogram: No route to host.");
    }

    [TestMethod]
    public async Task ComposeAsync_IsNotSupported()
    {
        var provider = Provider(Answer);

        var result = await provider.ComposeAsync("key", "Dress the person.", [new ImageReference([1], "image/png")]);

        result.IsSuccess.Should().BeFalse();
        sent.Should().BeEmpty();
    }

    [TestMethod]
    public void Provider_IsIdeogramAndLeavesTheBackgroundTransparent()
    {
        var provider = Provider(Answer);

        provider.Provider.Should().Be(ImageGenerationProviders.Ideogram);
        provider.SupportsTransparentBackground.Should().BeTrue();
    }

    private static string CompletedWith(string url) =>
        $$"""{"generation_id":"g1","status":"completed","created":"2026-10-21T00:00:00Z","response_type":"url","usage_cost_usd_micros":30000,"data":[{"prompt":"{\"high_level_description\":\"A fox.\"}","resolution":"2048x2048","is_image_safe":true,"seed":7,"url":"{{url}}"}]}""";

    // The API accepts the request, the generation is done when asked about, and its link answers with the image.
    private static HttpResponseMessage Answer(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post ? Json(HttpStatusCode.OK, Accepted)
        : request.RequestUri!.AbsoluteUri == GenerationUrl ? Json(HttpStatusCode.OK, Completed)
        : new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Image) { Headers = { ContentType = new("image/png") } }
        };

    private IdeogramImageProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> send, IdeogramOptions? settings = null)
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(x => x.CreateClient("Ideogram")).Returns(() => new HttpClient(new Handler(async request =>
        {
            sent.Add(request);
            if (request.Content is not null && request.Method == HttpMethod.Post)
                body = await request.Content.ReadAsStringAsync();
            return send(request);
        })));
        settings ??= new IdeogramOptions();
        settings.RetryBaseDelay = TimeSpan.Zero;
        settings.PollInterval = TimeSpan.Zero;
        return new IdeogramImageProvider(clients.Object, Microsoft.Extensions.Options.Options.Create(settings));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
