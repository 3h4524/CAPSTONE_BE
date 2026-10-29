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
}
