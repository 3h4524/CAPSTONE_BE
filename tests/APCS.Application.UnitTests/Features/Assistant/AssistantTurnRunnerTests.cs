using System.Runtime.CompilerServices;
using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Rag.Dtos;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant;

[TestClass]
public sealed class AssistantTurnRunnerTests
{
    private readonly Mock<IRepository<AssistantConversation>> conversations = new();
    private readonly Mock<IRepository<AssistantMessage>> messages = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IRagService> rag = new();
    private readonly Mock<ICacheService> cache = new();
    private readonly FakeTurnBuffer buffer = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
    private readonly List<AssistantMessage> saved = [];

    public AssistantTurnRunnerTests()
    {
        messages.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => new AssistantMessage { Id = (Guid)id, Role = "assistant", Content = string.Empty, Metadata = "{}" });
        messages.Setup(r => r.UpdateAsync(It.IsAny<AssistantMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantMessage, bool, CancellationToken>((message, _, _) => saved.Add(message))
            .Returns(Task.CompletedTask);
        conversations.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AssistantConversation());
        rag.Setup(r => r.SearchAsync(It.IsAny<RagSearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private AssistantTurnRunner Runner(IAssistantChatModel model) => new(
        new AssistantAgentLoop(
            model,
            new AssistantPromptBuilder(time),
            new AssistantToolRegistry([]),
            new AssistantKnowledgeBase(rag.Object, cache.Object, NullLogger<AssistantKnowledgeBase>.Instance),
            conversations.Object,
            messages.Object,
            unitOfWork.Object,
            time,
            NullLogger<AssistantAgentLoop>.Instance),
        buffer,
        Options.Create(new AssistantTurnOptions { TurnTimeoutSeconds = 60 }),
        time,
        NullLogger<AssistantTurnRunner>.Instance);

    private static string? Status(AssistantMessage message) =>
        AssistantJson.Deserialize<AssistantMessageMetadata>(message.Metadata)!.Status;

    private static AssistantErrorData ErrorOf(AssistantTurnEntry entry) =>
        JsonSerializer.Deserialize<AssistantErrorData>(entry.Data, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [TestMethod]
    public async Task RunAsync_AppendsEveryEventAndSavesTheRowAsDone()
    {
        var session = AssistantTestSupport.Session();
        var model = new FakeChatModel([AssistantTestSupport.Text("Hel"), AssistantTestSupport.Text("lo"), AssistantTestSupport.Done()]);

        await Runner(model).RunAsync(session, CancellationToken.None);

        var entries = buffer.Entries(session.AssistantMessageId);
        entries.Select(entry => entry.Type).Should().Equal("tool_started", "tool_finished", "delta", "delta", "done");
        entries.Last().Data.Should().Contain(session.AssistantMessageId.ToString());
        saved.Should().ContainSingle().Which.Should().Match<AssistantMessage>(message =>
            message.Id == session.AssistantMessageId && message.Content == "Hello");
        Status(saved.Single()).Should().Be(AssistantMessageStatuses.Done);
    }

    [TestMethod]
    public async Task RunAsync_WhenStopIsRequested_SavesPartialTextAsStoppedAndEndsWithStoppedError()
    {
        var session = AssistantTestSupport.Session();
        buffer.OnAppend = entry =>
        {
            if (entry.Type == "delta")
                _ = buffer.RequestStopAsync(session.AssistantMessageId);
        };

        await Runner(new HangingChatModel("partial")).RunAsync(session, CancellationToken.None);

        var last = buffer.Entries(session.AssistantMessageId).Last();
        last.Type.Should().Be("error");
        ErrorOf(last).Code.Should().Be(AssistantErrors.StoppedCode);
        saved.Should().ContainSingle().Which.Content.Should().Be("partial");
        Status(saved.Single()).Should().Be(AssistantMessageStatuses.Stopped);
        AssistantJson.Deserialize<AssistantMessageMetadata>(saved.Single().Metadata)!.Error.Should().BeNull();
    }

    [TestMethod]
    public async Task RunAsync_WhenStopWasRequestedBeforeTheTurnStarted_StopsAtOnce()
    {
        var session = AssistantTestSupport.Session();
        await buffer.RequestStopAsync(session.AssistantMessageId);

        await Runner(new HangingChatModel("partial")).RunAsync(session, CancellationToken.None);

        ErrorOf(buffer.Entries(session.AssistantMessageId).Last()).Code.Should().Be(AssistantErrors.StoppedCode);
        Status(saved.Single()).Should().Be(AssistantMessageStatuses.Stopped);
    }

    [TestMethod]
    public async Task RunAsync_WhenTheTurnTimesOut_SavesAnErrorAndEndsWithTimeoutError()
    {
        var session = AssistantTestSupport.Session();
        buffer.OnAppend = entry =>
        {
            if (entry.Type == "delta")
                time.Advance(TimeSpan.FromSeconds(61));
        };

        await Runner(new HangingChatModel("partial")).RunAsync(session, CancellationToken.None);

        ErrorOf(buffer.Entries(session.AssistantMessageId).Last()).Code.Should().Be(AssistantErrors.TimeoutCode);
        Status(saved.Single()).Should().Be(AssistantMessageStatuses.Error);
        AssistantJson.Deserialize<AssistantMessageMetadata>(saved.Single().Metadata)!.Error!.Code.Should().Be(AssistantErrors.TimeoutCode);
    }

    [TestMethod]
    public async Task RunAsync_WhenTheHostStops_SavesTheTurnAsInterrupted()
    {
        var session = AssistantTestSupport.Session();
        using var stopping = new CancellationTokenSource();
        buffer.OnAppend = entry =>
        {
            if (entry.Type == "delta")
                stopping.Cancel();
        };

        await Runner(new HangingChatModel("partial")).RunAsync(session, stopping.Token);

        ErrorOf(buffer.Entries(session.AssistantMessageId).Last()).Code.Should().Be(AssistantErrors.InterruptedCode);
        Status(saved.Single()).Should().Be(AssistantMessageStatuses.Interrupted);
    }

    [TestMethod]
    public async Task RunAsync_WhenTheModelFails_SavesTheRowBeforeAppendingTheError()
    {
        var session = AssistantTestSupport.Session();
        var savedBeforeError = -1;
        buffer.OnAppend = entry =>
        {
            if (entry.Type == "error")
                savedBeforeError = saved.Count;
        };

        await Runner(new FakeChatModel([AssistantModelUpdate.Failed("model_unavailable")])).RunAsync(session, CancellationToken.None);

        savedBeforeError.Should().Be(1);
        Status(saved.Single()).Should().Be(AssistantMessageStatuses.Error);
        buffer.Entries(session.AssistantMessageId).Should().ContainSingle(entry => entry.Type == "error");
        buffer.Entries(session.AssistantMessageId).Should().NotContain(entry => entry.Type == "done");
    }

    [TestMethod]
    public async Task RunAsync_WhenTheModelThrows_SavesAnErrorAndEndsWithAnErrorEvent()
    {
        var session = AssistantTestSupport.Session();

        await Runner(new ThrowingChatModel()).RunAsync(session, CancellationToken.None);

        ErrorOf(buffer.Entries(session.AssistantMessageId).Last()).Code.Should().Be(AssistantErrors.ModelUnavailableCode);
        Status(saved.Single()).Should().Be(AssistantMessageStatuses.Error);
    }

    [TestMethod]
    public async Task RunAsync_WhenItEnds_StopsWatchingForAStop()
    {
        var session = AssistantTestSupport.Session();

        await Runner(new FakeChatModel([AssistantTestSupport.Text("ok"), AssistantTestSupport.Done()])).RunAsync(session, CancellationToken.None);

        buffer.StopWatchers.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    private sealed class HangingChatModel(string firstText) : IAssistantChatModel
    {
        public string ModelName => "hanging-model";

        public async IAsyncEnumerable<AssistantModelUpdate> StreamAsync(
            AssistantModelRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return AssistantTestSupport.Text(firstText);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield return AssistantTestSupport.Done();
        }
    }

    private sealed class ThrowingChatModel : IAssistantChatModel
    {
        public string ModelName => "throwing-model";

        public async IAsyncEnumerable<AssistantModelUpdate> StreamAsync(
            AssistantModelRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (request.Messages.Count > 0)
                throw new InvalidOperationException("boom");
            yield break;
        }
    }
}
