using System.Net;
using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Google.GenAI;
using Microsoft.Extensions.Logging.Abstractions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class GeminiAssistantChatModelTests
{
    [TestMethod]
    public async Task StreamAsync_WhenApiKeyMissing_YieldsNotConfiguredFailureOnly()
    {
        var model = new GeminiAssistantChatModel(Microsoft.Extensions.Options.Options.Create(new AssistantOptions()), NullLogger<GeminiAssistantChatModel>.Instance);
        var request = new AssistantModelRequest([AssistantModelMessage.User("hi")], [], AllowTools: false);

        var updates = new List<AssistantModelUpdate>();
        await foreach (var update in model.StreamAsync(request))
            updates.Add(update);

        updates.Should().ContainSingle().Which.Should().Match<AssistantModelUpdate>(update =>
            update.Kind == AssistantModelUpdateKind.Failed && update.FailureCode == "not_configured");
    }

    [TestMethod]
    public void ModelName_ReturnsConfiguredModel()
    {
        var model = new GeminiAssistantChatModel(
            Microsoft.Extensions.Options.Options.Create(new AssistantOptions { Model = "gemini-x" }),
            NullLogger<GeminiAssistantChatModel>.Instance);

        model.ModelName.Should().Be("gemini-x");
    }

    [TestMethod]
    [DataRow(429, true)]
    [DataRow(400, false)]
    [DataRow(403, false)]
    [DataRow(503, false)]
    public void IsRateLimited_RecognisesQuotaFailuresOnly(int statusCode, bool expected) =>
        GeminiAssistantChatModel.IsRateLimited(new ClientError("error", statusCode, "STATUS", null!)).Should().Be(expected);

    [TestMethod]
    public void IsRateLimited_ReadsHttpRequestExceptionStatusCode() =>
        GeminiAssistantChatModel.IsRateLimited(new HttpRequestException("error", null, HttpStatusCode.TooManyRequests)).Should().BeTrue();

    [TestMethod]
    [DataRow("You exceeded your current quota (429)")]
    [DataRow("503 UNAVAILABLE: high demand")]
    public void Classification_IgnoresStatusTextInMessages(string message)
    {
        var exception = new InvalidOperationException(message);

        GeminiAssistantChatModel.IsRateLimited(exception).Should().BeFalse();
        GeminiAssistantChatModel.IsOverloaded(exception).Should().BeFalse();
    }

    [TestMethod]
    public void AssistantOptions_DefaultToTwoRetriesWithAnEightSecondPause()
    {
        var options = new AssistantOptions();

        options.RateLimitRetries.Should().Be(2);
        options.RateLimitRetryDelaySeconds.Should().Be(8);
    }

    [TestMethod]
    [DataRow(500, true)]
    [DataRow(503, true)]
    [DataRow(504, true)]
    [DataRow(501, false)]
    public void IsOverloaded_RecognisesTemporaryServerFailuresOnly(int statusCode, bool expected) =>
        GeminiAssistantChatModel.IsOverloaded(new ServerError("error", statusCode, "STATUS", null!)).Should().Be(expected);

    [TestMethod]
    [DataRow(400)]
    [DataRow(429)]
    public void IsOverloaded_IgnoresClientErrors(int statusCode) =>
        GeminiAssistantChatModel.IsOverloaded(new ClientError("error", statusCode, "STATUS", null!)).Should().BeFalse();

    [TestMethod]
    public void AssistantOptions_RetryOverloadAfterTwoSeconds() =>
        new AssistantOptions().OverloadRetryDelaySeconds.Should().Be(2);
}
