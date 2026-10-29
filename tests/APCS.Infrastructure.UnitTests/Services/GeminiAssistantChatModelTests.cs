using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
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
    [DataRow("You exceeded your current quota, please check your plan", true)]
    [DataRow("HTTP 429 Too Many Requests", true)]
    [DataRow("RESOURCE_EXHAUSTED", true)]
    [DataRow("API key not valid", false)]
    [DataRow("Invalid argument", false)]
    public void IsRateLimited_RecognisesQuotaFailuresOnly(string message, bool expected) =>
        GeminiAssistantChatModel.IsRateLimited(new InvalidOperationException(message)).Should().Be(expected);

    [TestMethod]
    public void AssistantOptions_DefaultToTwoRetriesWithAnEightSecondPause()
    {
        var options = new AssistantOptions();

        options.RateLimitRetries.Should().Be(2);
        options.RateLimitRetryDelaySeconds.Should().Be(8);
    }

    [TestMethod]
    [DataRow("This model is currently experiencing high demand. Spikes in demand are usually temporary.", true)]
    [DataRow("The model is overloaded. Please try again later.", true)]
    [DataRow("503 UNAVAILABLE", true)]
    [DataRow("API key not valid", false)]
    [DataRow("Invalid argument", false)]
    public void IsOverloaded_RecognisesTemporaryServerFailuresOnly(string message, bool expected) =>
        GeminiAssistantChatModel.IsOverloaded(new InvalidOperationException(message)).Should().Be(expected);

    [TestMethod]
    public void AssistantOptions_RetryOverloadAfterTwoSeconds() =>
        new AssistantOptions().OverloadRetryDelaySeconds.Should().Be(2);
}
