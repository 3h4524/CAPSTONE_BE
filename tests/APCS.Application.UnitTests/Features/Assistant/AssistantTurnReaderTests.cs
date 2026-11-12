using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Assistant;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace APCS.Application.UnitTests.Features.Assistant;

[TestClass]
public sealed class AssistantTurnReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTurnBuffer buffer = new();
    private readonly FakeTimeProvider time = new(Now);
    private readonly Guid turnId = Guid.NewGuid();

    private AssistantTurnReader Reader() => new(buffer, time);

    private static async Task<List<AssistantTurnEntry>> Collect(IAsyncEnumerable<AssistantTurnEntry> entries)
    {
        var list = new List<AssistantTurnEntry>();
        await foreach (var entry in entries)
            list.Add(entry);
        return list;
    }

    private Task<string> Append(string type, string data = "{}") => buffer.AppendAsync(turnId, type, data);

    [TestMethod]
    public async Task ReadAsync_FromTheStart_ReplaysEveryEntryAndEndsAfterTheTerminalEvent()
    {
        await Append("conversation");
        await Append("delta");
        await Append("done");
        await Append("delta");

        var entries = await Collect(Reader().ReadAsync(turnId, null, Now.UtcDateTime.AddMinutes(5)));

        entries.Select(entry => entry.Type).Should().Equal("conversation", "delta", "done");
        entries.Select(entry => entry.Id).Should().Equal("1-0", "2-0", "3-0");
    }

    [TestMethod]
    public async Task ReadAsync_AfterAnId_ReplaysOnlyTheLaterEntries()
    {
        await Append("conversation");
        var seen = await Append("delta", """{"text":"Hel"}""");
        await Append("delta", """{"text":"lo"}""");
        await Append("error");

        var entries = await Collect(Reader().ReadAsync(turnId, seen, Now.UtcDateTime.AddMinutes(5)));

        entries.Select(entry => entry.Data).Should().Equal("""{"text":"lo"}""", "{}");
    }

    [TestMethod]
    public async Task ReadAsync_WhileTheTurnRuns_TailsNewEntriesUntilDone()
    {
        await Append("conversation");
        var reading = Collect(Reader().ReadAsync(turnId, null, Now.UtcDateTime.AddMinutes(5)));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1);

        await Append("delta");
        await Append("done");

        (await reading.WaitAsync(TimeSpan.FromSeconds(5))).Select(entry => entry.Type).Should().Equal("conversation", "delta", "done");
        buffer.AppendWatchers.Single().Disposed.Should().BeTrue();
    }

    [TestMethod]
    public async Task ReadAsync_WhenANotificationIsLost_StillPicksUpTheEntryOnTheFallbackPoll()
    {
        await Append("conversation");
        var reading = Collect(Reader().ReadAsync(turnId, null, Now.UtcDateTime.AddMinutes(5)));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1 && buffer.AppendWatchers[0].Waits > 0);
        buffer.AppendWatchers.Clear();

        await Append("done");

        (await reading.WaitAsync(TimeSpan.FromSeconds(5))).Select(entry => entry.Type).Should().Equal("conversation", "done");
    }

    [TestMethod]
    public async Task ReadAsync_WhenTheStreamExpires_Ends()
    {
        await Append("conversation");
        var reading = Collect(Reader().ReadAsync(turnId, null, Now.UtcDateTime.AddMinutes(5)));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1);

        buffer.Expire(turnId);

        (await reading.WaitAsync(TimeSpan.FromSeconds(5))).Select(entry => entry.Type).Should().Equal("conversation");
    }

    [TestMethod]
    public async Task ReadAsync_WhenTheDeadlinePassesWithoutATerminalEvent_Ends()
    {
        await Append("conversation");

        var entries = await Collect(Reader().ReadAsync(turnId, null, Now.UtcDateTime));

        entries.Select(entry => entry.Type).Should().Equal("conversation");
    }

    [TestMethod]
    public async Task ReadAsync_WhenTheClientDisconnects_StopsReadingButLeavesTheTurnAlone()
    {
        await Append("conversation");
        using var disconnect = new CancellationTokenSource();
        var reading = Collect(Reader().ReadAsync(turnId, null, Now.UtcDateTime.AddMinutes(5), disconnect.Token));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1);

        await disconnect.CancelAsync();

        await FluentActions.Awaiting(() => reading).Should().ThrowAsync<OperationCanceledException>();
        buffer.AppendWatchers.Single().Disposed.Should().BeTrue();
        (await buffer.IsStopRequestedAsync(turnId)).Should().BeFalse();
        (await buffer.ExistsAsync(turnId)).Should().BeTrue();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(10);
        }
    }
}
