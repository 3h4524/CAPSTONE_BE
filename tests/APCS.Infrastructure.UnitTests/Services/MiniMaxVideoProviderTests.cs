using System.Net;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class MiniMaxVideoProviderTests
{
    private readonly List<CapturedRequest> requests = [];

    [TestMethod]
    public async Task StartAsync_ApprovedFirstFrame_SendsH3At768PAndReturnsTaskId()
    {
        var png = Png(1200, 1200);
        var provider = Provider(request => request.RequestUri!.Host == "signed.example"
            ? Bytes(png)
            : Json(HttpStatusCode.OK, """{"task_id":"task_123"}"""));

        var taskId = await provider.StartAsync("mini-key", "https://signed.example/source.png",
            "A person naturally wears the product.", "9:16", 6, true, new Region(0, 0, 1, 1), default);

        taskId.Should().Be("task_123");
        var submitted = requests.Single(x => x.Method == HttpMethod.Post);
        submitted.Url.Should().Be("https://api.minimax.io/v2/video_generation");
        submitted.Authorization.Should().Be("Bearer mini-key");
        using var body = JsonDocument.Parse(submitted.Body);
        body.RootElement.GetProperty("model").GetString().Should().Be("MiniMax-H3");
        body.RootElement.GetProperty("resolution").GetString().Should().Be("768P");
        body.RootElement.GetProperty("duration").GetInt32().Should().Be(6);
        body.RootElement.GetProperty("ratio").GetString().Should().Be("adaptive");
        var content = body.RootElement.GetProperty("content");
        content[0].GetProperty("text").GetString().Should().Be("A person naturally wears the product.");
        content[1].GetProperty("role").GetString().Should().Be("first_frame");
        var uri = content[1].GetProperty("image_url").GetProperty("url").GetString()!;
        uri.Should().StartWith("data:image/png;base64,");
        using var firstFrame = Image.Load(Convert.FromBase64String(uri["data:image/png;base64,".Length..]));
        firstFrame.Width.Should().Be(675);
        firstFrame.Height.Should().Be(1200);
    }

    [TestMethod]
    [DataRow(5, "9:16", "Move naturally.")]
    [DataRow(8, "1:1", "Move naturally.")]
    [DataRow(8, "9:16", "")]
    public async Task StartAsync_UnsupportedSettings_AreRejectedBeforeAnyRequest(int seconds, string aspect, string prompt)
    {
        var provider = Provider(_ => Bytes(Png(400, 400)));

        var action = () => provider.StartAsync("key", "https://signed.example/source.png", prompt, aspect, seconds, true, default);

        (await action.Should().ThrowAsync<AiVideoNotSubmittedException>()).WithInnerException<ArgumentException>();
        requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_SourceIsNotAnApprovedPng_IsNotSubmitted()
    {
        var provider = Provider(_ => Bytes(Encoding.UTF8.GetBytes("not an image")));

        var action = () => provider.StartAsync("key", "https://signed.example/source.png", "Move naturally.", "16:9", 4, true, default);

        await action.Should().ThrowAsync<AiVideoNotSubmittedException>();
        requests.Should().ContainSingle(x => x.Method == HttpMethod.Get && x.Url.Contains("signed.example"));
    }

    [TestMethod]
    public async Task StartAsync_ProviderRefuses_ReportsStatusAndDetail()
    {
        var provider = Provider(request => request.RequestUri!.Host == "signed.example"
            ? Bytes(Png(800, 800))
            : Json(HttpStatusCode.TooManyRequests, """{"type":"error","error":{"message":"rate limit, please retry later"}}"""));

        var action = () => provider.StartAsync("key", "https://signed.example/source.png", "Move naturally.", "9:16", 8, true, default);

        var refusal = (await action.Should().ThrowAsync<AiVideoProviderException>()).Which;
        refusal.StatusCode.Should().Be(429);
        refusal.Detail.Should().Be("rate limit, please retry later");
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"task_id\":\"../secret\"}")]
    [DataRow("not json")]
    public async Task StartAsync_AcceptedWithoutUsableTaskId_IsAnUnknownSubmission(string response)
    {
        var provider = Provider(request => request.RequestUri!.Host == "signed.example"
            ? Bytes(Png(800, 800))
            : Json(HttpStatusCode.OK, response));

        var action = () => provider.StartAsync("key", "https://signed.example/source.png", "Move naturally.", "9:16", 8, true, default);

        await action.Should().ThrowAsync<AiVideoSubmissionUnknownException>();
    }

    [TestMethod]
    [DataRow("queued")]
    [DataRow("running")]
    public async Task PollAsync_UnfinishedTask_RemainsInProgress(string status)
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK,
            $"{{\"task\":{{\"id\":\"task_123\",\"model\":\"MiniMax-H3\",\"status\":\"{status}\"}}}}"));

        (await provider.PollAsync("key", "task_123", default)).Should().Be(new VeoOperationResult(false, null, null));
    }

    [TestMethod]
    public async Task PollAsync_SucceededTask_ReturnsTemporaryVideoUrl()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK,
            """{"task":{"id":"task_123","model":"MiniMax-H3","status":"succeeded","content":{"url":"https://cdn.hailuoai.com/output.mp4"}}}"""));

        (await provider.PollAsync("key", "task_123", default)).Should()
            .Be(new VeoOperationResult(true, "https://cdn.hailuoai.com/output.mp4", null));
    }

    [TestMethod]
    public async Task PollAsync_FailedTask_ReturnsProviderReasonWithoutClaimingNoCharge()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK,
            """{"task":{"id":"task_123","model":"MiniMax-H3","status":"failed","error":{"code":"1026","message":"sensitive content"}}}"""));

        var result = await provider.PollAsync("key", "task_123", default);

        result.Done.Should().BeTrue();
        result.VideoUri.Should().BeNull();
        result.Failure.Should().Be("sensitive content (1026)");
        result.NotCharged.Should().BeFalse();
    }

    [TestMethod]
    [DataRow("different_task", "MiniMax-H3")]
    [DataRow("task_123", "MiniMax-H3-Lite")]
    public async Task PollAsync_DifferentTaskOrModel_IsRejected(string returnedTask, string returnedModel)
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK,
            $"{{\"task\":{{\"id\":\"{returnedTask}\",\"model\":\"{returnedModel}\",\"status\":\"running\"}}}}"));

        var action = () => provider.PollAsync("key", "task_123", default);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task DownloadAsync_TrustedMiniMaxCdn_ReturnsMp4WithoutLeakingApiKey()
    {
        var clip = Mp4();
        var provider = Provider(_ => Bytes(clip));

        (await provider.DownloadAsync("secret-key", "https://video-product.cdn.minimax.io/output.mp4", default)).Should().Equal(clip);
        requests.Should().ContainSingle();
        requests[0].Authorization.Should().BeNull();
    }

    [TestMethod]
    [DataRow("http://cdn.hailuoai.com/output.mp4")]
    [DataRow("https://hailuoai.com.evil.example/output.mp4")]
    [DataRow("https://evil.example/output.mp4")]
    public async Task DownloadAsync_UntrustedUrl_IsNeverRequested(string url)
    {
        var provider = Provider(_ => Bytes(Mp4()));

        var action = () => provider.DownloadAsync("key", url, default);

        await action.Should().ThrowAsync<InvalidOperationException>();
        requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DownloadAsync_RedirectToUntrustedHost_IsRejected()
    {
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("https://evil.example/output.mp4") }
        });

        var action = () => provider.DownloadAsync("key", "https://cdn.hailuoai.com/output.mp4", default);

        await action.Should().ThrowAsync<InvalidOperationException>();
        requests.Should().ContainSingle();
    }

    private MiniMaxVideoProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Handler(async request =>
        {
            requests.Add(new(request.Method, request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync()));
            return send(request);
        })));
        return new(clients.Object, NullLogger<MiniMaxVideoProvider>.Instance);
    }

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] Mp4()
    {
        var clip = new byte[2048];
        "ftyp"u8.CopyTo(clip.AsSpan(4));
        return clip;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private sealed record CapturedRequest(HttpMethod Method, string Url, string? Authorization, string Body);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
