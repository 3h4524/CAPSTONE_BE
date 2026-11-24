using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiVideoCallbackServiceTests
{
    private static readonly Guid JobId = Guid.Parse("8d1f0d6e-34a2-4c55-9f1b-2a4b5c6d7e8f");
    private readonly Mock<IAiShotJobService> shots = new();

    private static AiVideoCallbackUrls Urls(string? baseUrl = "https://shop.example", string? secret = "test-secret") =>
        new(Options.Create(new AiVideoCallbackSettings { BaseUrl = baseUrl, Secret = secret }));

    private AiVideoCallbackService Service(AiVideoCallbackUrls? urls = null) => new(urls ?? Urls(), shots.Object);

    private static string Signature(AiVideoCallbackUrls urls, Guid jobId, int scene) => urls.For(jobId, scene)!.Split('/').Last();

    [TestMethod]
    [DataRow(null, "secret")]
    [DataRow("https://shop.example", null)]
    [DataRow("http://shop.example", "secret")]
    [DataRow("not a url", "secret")]
    public void For_WithoutAPublicHttpsAddressAndASecret_GivesNoUrl(string? baseUrl, string? secret)
    {
        Urls(baseUrl, secret).For(JobId, 0).Should().BeNull();
    }

    [TestMethod]
    public void For_GivesEachSceneItsOwnSignature()
    {
        var urls = Urls("https://shop.example/");

        var url = urls.For(JobId, 2)!;

        url.Should().StartWith($"https://shop.example/api/webhooks/minimax/{JobId:N}/2/");
        urls.Verify(JobId, 2, Signature(urls, JobId, 2)).Should().BeTrue();
        urls.Verify(JobId, 3, Signature(urls, JobId, 2)).Should().BeFalse("a signature is for one scene only");
        urls.Verify(Guid.NewGuid(), 2, Signature(urls, JobId, 2)).Should().BeFalse();
        Urls(secret: "another-secret").Verify(JobId, 2, Signature(urls, JobId, 2)).Should().BeFalse();
    }

    [TestMethod]
    public async Task ReceiveAsync_UrlItDidNotHandOut_IsUnknownAndReadsNothing()
    {
        var result = await Service().ReceiveAsync(JobId, 0, "forged", """{"challenge":"abc"}""", default);

        result.Outcome.Should().Be(AiVideoCallbackOutcome.Unknown);
        shots.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ReceiveAsync_CallbacksOff_IsUnknown()
    {
        var on = Urls();
        var result = await Service(Urls(secret: "")).ReceiveAsync(JobId, 0, Signature(on, JobId, 0), """{"challenge":"abc"}""", default);

        result.Outcome.Should().Be(AiVideoCallbackOutcome.Unknown);
    }

    [TestMethod]
    public async Task ReceiveAsync_UrlCheck_SendsTheChallengeBackWithoutTouchingAnyScene()
    {
        var urls = Urls();

        var result = await Service(urls).ReceiveAsync(JobId, 0, Signature(urls, JobId, 0), """{"challenge":"1a3cs1j-601a118y"}""", default);

        result.Should().Be(new AiVideoCallbackResult(AiVideoCallbackOutcome.Challenge, "1a3cs1j-601a118y"));
        shots.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow("succeeded")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    public async Task ReceiveAsync_TaskFinished_MarksTheSceneToBeAskedAboutAtOnce(string status)
    {
        var urls = Urls();
        shots.Setup(x => x.AnnounceClipAsync(JobId, 1, "task_123", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(true));

        var result = await Service(urls).ReceiveAsync(JobId, 1, Signature(urls, JobId, 1),
            """{"task":{"id":"task_123","model":"MiniMax-H3","status":"STATUS","content":{"url":"https://cdn.hailuoai.com/x.mp4"}}}"""
                .Replace("STATUS", status), default);

        result.Outcome.Should().Be(AiVideoCallbackOutcome.Accepted);
        shots.Verify(x => x.AnnounceClipAsync(JobId, 1, "task_123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("""{"task":{"id":"task_123","status":"running"}}""")]
    [DataRow("""{"task":{"id":"task_123","status":"queued"}}""")]
    [DataRow("""{"task":{"status":"succeeded"}}""")]
    [DataRow("not json")]
    public async Task ReceiveAsync_NothingFinishedToLookInto_IsAcceptedAndChangesNothing(string body)
    {
        var urls = Urls();

        var result = await Service(urls).ReceiveAsync(JobId, 0, Signature(urls, JobId, 0), body, default);

        result.Outcome.Should().Be(AiVideoCallbackOutcome.Accepted);
        shots.VerifyNoOtherCalls();
    }
}
