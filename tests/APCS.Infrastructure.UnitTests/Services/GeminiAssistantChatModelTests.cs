using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using APCS.Application.Abstractions.Ai;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Google.GenAI;
using Microsoft.Extensions.AI;
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

    [TestMethod]
    public async Task StreamAsync_WhenFirstKeyIsRateLimited_SwitchesToNextKeyWithoutWaiting()
    {
        var clients = new Dictionary<string, FakeChatClient>
        {
            ["key-1"] = FakeChatClient.Failing(new ClientError("quota", 429, "RESOURCE_EXHAUSTED", null!)),
            ["key-2"] = FakeChatClient.Answering("hello")
        };
        var model = CreateModel(new AssistantOptions { ApiKeys = ["key-1", "key-2"], RateLimitRetryDelaySeconds = 30 }, clients);
        var stopwatch = Stopwatch.StartNew();

        var updates = await CollectAsync(model);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        updates.Should().Contain(update => update.Kind == AssistantModelUpdateKind.Text && update.Text == "hello");
        updates.Should().NotContain(update => update.Kind == AssistantModelUpdateKind.Failed);
        clients["key-1"].Calls.Should().Be(1);
        clients["key-2"].Calls.Should().Be(1);
    }

    [TestMethod]
    public async Task StreamAsync_AfterSwitchingKeys_StartsTheNextRequestWithTheWorkingKey()
    {
        var clients = new Dictionary<string, FakeChatClient>
        {
            ["key-1"] = FakeChatClient.Failing(new ClientError("quota", 429, "RESOURCE_EXHAUSTED", null!)),
            ["key-2"] = FakeChatClient.Answering("hello")
        };
        var model = CreateModel(new AssistantOptions { ApiKeys = ["key-1", "key-2"] }, clients);

        await CollectAsync(model);
        await CollectAsync(model);

        clients["key-1"].Calls.Should().Be(1);
        clients["key-2"].Calls.Should().Be(2);
    }

    [TestMethod]
    public async Task StreamAsync_WhenEveryKeyIsRateLimitedAndNoRetriesLeft_Fails()
    {
        var clients = new Dictionary<string, FakeChatClient>
        {
            ["key-1"] = FakeChatClient.Failing(new ClientError("quota", 429, "RESOURCE_EXHAUSTED", null!)),
            ["key-2"] = FakeChatClient.Failing(new ClientError("quota", 429, "RESOURCE_EXHAUSTED", null!))
        };
        var model = CreateModel(new AssistantOptions { ApiKeys = ["key-1", "key-2"], RateLimitRetries = 0 }, clients);

        var updates = await CollectAsync(model);

        updates.Should().ContainSingle().Which.Should().Match<AssistantModelUpdate>(update =>
            update.Kind == AssistantModelUpdateKind.Failed && update.FailureCode == "model_unavailable");
        clients["key-1"].Calls.Should().Be(1);
        clients["key-2"].Calls.Should().Be(1);
    }

    [TestMethod]
    public void EffectiveApiKeys_SkipsBlankAndDuplicateKeysAndAppendsApiKey()
    {
        var options = new AssistantOptions { ApiKeys = [" key-1 ", "", "key-2", "key-1"], ApiKey = "key-3" };

        options.EffectiveApiKeys.Should().Equal("key-1", "key-2", "key-3");
        options.IsConfigured.Should().BeTrue();
    }

    private static GeminiAssistantChatModel CreateModel(AssistantOptions options, IReadOnlyDictionary<string, FakeChatClient> clients) =>
        new(Microsoft.Extensions.Options.Options.Create(options), NullLogger<GeminiAssistantChatModel>.Instance, apiKey => clients[apiKey]);

    private static async Task<List<AssistantModelUpdate>> CollectAsync(GeminiAssistantChatModel model)
    {
        var updates = new List<AssistantModelUpdate>();
        await foreach (var update in model.StreamAsync(new AssistantModelRequest([AssistantModelMessage.User("hi")], [], AllowTools: false)))
            updates.Add(update);
        return updates;
    }

    private sealed class FakeChatClient(Exception? failure, string? text) : IChatClient
    {
        public int Calls { get; private set; }

        public static FakeChatClient Failing(Exception failure) => new(failure, null);

        public static FakeChatClient Answering(string text) => new(null, text);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            if (failure is not null)
                throw failure;
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
