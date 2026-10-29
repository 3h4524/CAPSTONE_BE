using System.Net;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class VeoVideoProviderTests
{
    private const string Operation = "models/veo-3.1-lite-generate-preview/operations/abc123";
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, .. new byte[32]];

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> bodies = [];

    [TestMethod]
    public async Task StartAsync_AcceptedRequest_SendsTheSourceImageAndReturnsTheOperation()
    {
        var provider = Provider(request => request.RequestUri!.Host == "signed.example"
            ? Bytes(Png)
            : Json(HttpStatusCode.OK, $$"""{"name":"{{Operation}}"}"""));

        var operation = await provider.StartAsync("key", "https://signed.example/source.png", "Move gently.", "9:16", 8, true, default);

        operation.Should().Be(Operation);
        var submitted = requests.Single(x => x.Method == HttpMethod.Post);
        submitted.RequestUri!.AbsoluteUri.Should().EndWith("models/veo-3.1-lite-generate-preview:predictLongRunning");
        submitted.Headers.GetValues("x-goog-api-key").Should().Equal("key");
        using var body = JsonDocument.Parse(bodies.Single(x => x.Length > 0));
        var instance = body.RootElement.GetProperty("instances")[0];
        instance.GetProperty("prompt").GetString().Should().Be("Move gently.");
        instance.GetProperty("image").GetProperty("bytesBase64Encoded").GetString().Should().Be(Convert.ToBase64String(Png));
        var parameters = body.RootElement.GetProperty("parameters");
        parameters.GetProperty("aspectRatio").GetString().Should().Be("9:16");
        parameters.GetProperty("durationSeconds").GetInt32().Should().Be(8);
    }

    [TestMethod]
    [DataRow(5, "9:16")]
    [DataRow(8, "1:1")]
    public async Task StartAsync_UnsupportedSettings_AreRejectedBeforeAnyRequest(int seconds, string aspectRatio)
    {
        var provider = Provider(_ => Bytes(Png));

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", aspectRatio, seconds, false, default);

        (await act.Should().ThrowAsync<AiVideoNotSubmittedException>()).WithInnerException<ArgumentException>();
        requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_SourceIsNotAPng_IsNotSubmitted()
    {
        var provider = Provider(_ => Bytes(Encoding.UTF8.GetBytes("this is plainly not a PNG image file")));

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", "16:9", 4, false, default);

        (await act.Should().ThrowAsync<AiVideoNotSubmittedException>()).WithInnerException<InvalidOperationException>();
        requests.Should().OnlyContain(x => x.Method == HttpMethod.Get);
    }

    [TestMethod]
    public async Task StartAsync_SourceCannotBeRead_IsNotSubmitted()
    {
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", "16:9", 4, false, default);

        // Google was never called, so the scene is certain not to have been billed.
        await act.Should().ThrowAsync<AiVideoNotSubmittedException>();
        requests.Should().OnlyContain(x => x.Method == HttpMethod.Get);
    }

    [TestMethod]
    public async Task StartAsync_GoogleRefuses_ReportsItsStatus()
    {
        var provider = Provider(request => request.Method == HttpMethod.Get ? Bytes(Png) : Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"Quota exceeded."}}"""));

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", "9:16", 6, false, default);

        var refusal = (await act.Should().ThrowAsync<VeoProviderException>()).Which;
        refusal.StatusCode.Should().Be(429);
        refusal.Detail.Should().Be("Quota exceeded.");
    }

    [TestMethod]
    public async Task StartAsync_RefusalWithoutAReadableBody_HasNoDetail()
    {
        var provider = Provider(request => request.Method == HttpMethod.Get ? Bytes(Png) : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("<html>Bad Request</html>") });

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", "9:16", 6, false, default);

        var refusal = (await act.Should().ThrowAsync<VeoProviderException>()).Which;
        refusal.StatusCode.Should().Be(400);
        refusal.Detail.Should().BeNull();
    }

    [TestMethod]
    [DataRow("""{"name":"operations/../secrets"}""")]
    [DataRow("""{"done":false}""")]
    [DataRow("not json")]
    public async Task StartAsync_AcceptedWithoutAUsableOperationName_IsAnUnknownSubmission(string response)
    {
        var provider = Provider(request => request.Method == HttpMethod.Get ? Bytes(Png) : Json(HttpStatusCode.OK, response));

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", "9:16", 8, false, default);

        // Google took the request, so a clip may be billed: this must not look like an ordinary refusal.
        await act.Should().ThrowAsync<AiVideoSubmissionUnknownException>();
    }

    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.GatewayTimeout)]
    public async Task StartAsync_GoogleServerError_IsReportedAsARefusal(HttpStatusCode status)
    {
        var provider = Provider(request => request.Method == HttpMethod.Get ? Bytes(Png) : Json(status, """{"error":{"message":"Internal error encountered."}}"""));

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", "9:16", 8, false, default);

        (await act.Should().ThrowAsync<VeoProviderException>()).Which.StatusCode.Should().Be((int)status);
    }

    [TestMethod]
    public async Task StartAsync_GoogleCannotBeReached_IsNotSubmitted()
    {
        var provider = Provider(request => request.Method == HttpMethod.Get
            ? Bytes(Png)
            : throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made."));

        var act = () => provider.StartAsync("key", "https://signed.example/source.png", "Move.", "9:16", 8, false, default);

        await act.Should().ThrowAsync<AiVideoNotSubmittedException>();
    }

    [TestMethod]
    public async Task PollAsync_OperationStillRunning_IsNotDone()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, $$"""{"name":"{{Operation}}"}"""));

        (await provider.PollAsync("key", Operation, default)).Should().Be(new VeoOperationResult(false, null, null));
        requests.Single().RequestUri!.AbsoluteUri.Should().EndWith(Operation);
    }

    [TestMethod]
    public async Task PollAsync_FinishedOperation_ReturnsTheVideoAddress()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK,
            """{"done":true,"response":{"generateVideoResponse":{"generatedSamples":[{"video":{"uri":"https://generativelanguage.googleapis.com/clip"}}]}}}"""));

        (await provider.PollAsync("key", Operation, default)).Should().Be(new VeoOperationResult(true, "https://generativelanguage.googleapis.com/clip", null));
    }

    [TestMethod]
    public async Task PollAsync_ClipRemovedByTheSafetyFilter_ReportsTheReasonGoogleGave()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK,
            """{"done":true,"response":{"generateVideoResponse":{"raiMediaFilteredCount":1,"raiMediaFilteredReasons":["The image\n shows a minor."]}}}"""));

        var result = await provider.PollAsync("key", Operation, default);

        result.Done.Should().BeTrue();
        result.VideoUri.Should().BeNull();
        result.Failure.Should().Be("The image shows a minor.");
        // Google did not say the attempt was free, so it is not assumed to be.
        result.NotCharged.Should().BeFalse();
    }

    [TestMethod]
    public async Task PollAsync_ClipDroppedWithoutCharge_SaysSo()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK,
            """{"done":true,"response":{"generateVideoResponse":{"raiMediaFilteredCount":1,"raiMediaFilteredReasons":["We encountered an issue with the audio for your prompt. You have not been charged for this attempt."]}}}"""));

        var result = await provider.PollAsync("key", Operation, default);

        result.VideoUri.Should().BeNull();
        result.Failure.Should().Contain("issue with the audio");
        result.NotCharged.Should().BeTrue();
    }

    [TestMethod]
    public async Task PollAsync_FinishedWithNeitherClipNorReason_ReportsNoVideo()
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, """{"done":true,"response":{"generateVideoResponse":{}}}"""));

        var result = await provider.PollAsync("key", Operation, default);

        result.Failure.Should().Be("Veo returned no video.");
        result.NotCharged.Should().BeFalse();
    }

    [TestMethod]
    [DataRow("""{"done":true,"error":{"code":3,"message":"Unsafe."}}""", "Unsafe.")]
    [DataRow("""{"done":true,"error":{"code":3}}""", "could not generate this scene")]
    public async Task PollAsync_OperationFailed_ReportsAFailure(string response, string message)
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, response));

        var result = await provider.PollAsync("key", Operation, default);

        result.Done.Should().BeTrue();
        result.VideoUri.Should().BeNull();
        result.Failure.Should().Contain(message);
    }

    [TestMethod]
    public async Task PollAsync_GoogleRefuses_ReportsItsStatus()
    {
        var provider = Provider(_ => Json(HttpStatusCode.Forbidden, """{"error":{"message":"No access."}}"""));

        var act = () => provider.PollAsync("key", Operation, default);

        (await act.Should().ThrowAsync<VeoProviderException>()).Which.StatusCode.Should().Be(403);
    }

    [TestMethod]
    [DataRow("https://evil.example/operations/abc")]
    [DataRow("models/veo/operations/../../keys")]
    [DataRow("operations/abc?alt=media")]
    public async Task PollAsync_UnexpectedOperationName_IsNeverRequested(string operationName)
    {
        var provider = Provider(_ => Json(HttpStatusCode.OK, "{}"));

        var act = () => provider.PollAsync("key", operationName, default);

        await act.Should().ThrowAsync<ArgumentException>();
        requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DownloadAsync_FollowsGoogleRedirectsAndReturnsTheClip()
    {
        var clip = Mp4();
        var provider = Provider(request => request.RequestUri!.Host == "generativelanguage.googleapis.com"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://storage.googleapis.com/bucket/clip.mp4") } }
            : Bytes(clip));

        (await provider.DownloadAsync("key", "https://generativelanguage.googleapis.com/files/clip:download", default)).Should().Equal(clip);
        requests.Select(x => x.RequestUri!.Host).Should().Equal("generativelanguage.googleapis.com", "storage.googleapis.com");
    }

    [TestMethod]
    [DataRow("not a url")]
    [DataRow("http://generativelanguage.googleapis.com/clip")]
    [DataRow("https://evil.example/clip")]
    public async Task DownloadAsync_UntrustedAddress_IsNeverRequested(string address)
    {
        var provider = Provider(_ => Bytes(Mp4()));

        var act = () => provider.DownloadAsync("key", address, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DownloadAsync_RedirectToAnotherHost_IsNotFollowed()
    {
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/clip.mp4") } });

        var act = () => provider.DownloadAsync("key", "https://generativelanguage.googleapis.com/clip", default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        requests.Should().ContainSingle();
    }

    [TestMethod]
    public async Task DownloadAsync_EndlessRedirects_GiveUp()
    {
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/again", UriKind.Relative) } });

        var act = () => provider.DownloadAsync("key", "https://generativelanguage.googleapis.com/clip", default);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("redirects");
        requests.Should().HaveCount(4);
    }

    [TestMethod]
    public async Task DownloadAsync_ResponseIsNotAnMp4_IsRejected()
    {
        var provider = Provider(_ => Bytes(new byte[2048]));

        var act = () => provider.DownloadAsync("key", "https://generativelanguage.googleapis.com/clip", default);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("invalid MP4");
    }

    private static byte[] Mp4()
    {
        var clip = new byte[2048];
        "ftyp"u8.CopyTo(clip.AsSpan(4));
        return clip;
    }

    private VeoVideoProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Handler(async request =>
        {
            requests.Add(request);
            bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync());
            return send(request);
        })));
        return new(clients.Object, NullLogger<VeoVideoProvider>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
