using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.ListingStreams;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace APCS.Application.UnitTests.Features.ListingStreams;

[TestClass]
public sealed class ListingStreamReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeListingStreamBuffer buffer = new();
    private readonly FakeTimeProvider time = new(Now);
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid streamId = Guid.NewGuid();

    private ListingStreamReader Reader() => new(buffer, time);

    private Task<string> Append(string type, string data = "{}") => buffer.AppendAsync(userId, streamId, type, data);

    private static async Task<List<EventStreamEntry>> Collect(IAsyncEnumerable<EventStreamEntry> entries)
    {
        var list = new List<EventStreamEntry>();
        await foreach (var entry in entries)
            list.Add(entry);
        return list;
    }

    [TestMethod]
    public async Task ReadAsync_FromTheStart_ReplaysEveryEntryAndStopsAtEnd()
    {
        await Append("start");
        await Append("delta");
        await Append("done");
        await Append("end");
        await Append("delta");

        var entries = await Collect(Reader().ReadAsync(userId, streamId, null));

        entries.Select(entry => entry.Type).Should().Equal("start", "delta", "done", "end");
    }

    [TestMethod]
    public async Task ReadAsync_OpenedBeforeTheFirstEvent_WaitsForTheRequestToWriteIt()
    {
        var reading = Collect(Reader().ReadAsync(userId, streamId, null));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1 && buffer.AppendWatchers[0].Waits > 0);

        await Append("start");
        await Append("end");

        (await reading.WaitAsync(TimeSpan.FromSeconds(5))).Select(entry => entry.Type).Should().Equal("start", "end");
        buffer.AppendWatchers.Single().Disposed.Should().BeTrue();
    }

    [TestMethod]
    public async Task ReadAsync_WhenNothingIsWrittenInTime_Ends()
    {
        var reading = Collect(Reader().ReadAsync(userId, streamId, null));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1 && buffer.AppendWatchers[0].Waits > 0);

        time.Advance(ListingStreamReader.FirstEntryTimeout);
        buffer.AppendWatchers[0].Wake();

        (await reading.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ReadAsync_AfterAnId_ReplaysOnlyTheLaterEntries()
    {
        await Append("start");
        var seen = await Append("delta", """{"text":"{\"ti"}""");
        await Append("delta", """{"text":"tle\""}""");
        await Append("end");

        var entries = await Collect(Reader().ReadAsync(userId, streamId, seen));

        entries.Select(entry => entry.Data).Should().Equal("""{"text":"tle\""}""", "{}");
    }

    [TestMethod]
    public async Task ReadAsync_ResumedAfterTheEnd_EndsAtOnce()
    {
        await Append("start");
        var end = await Append("end");

        var entries = await Collect(Reader().ReadAsync(userId, streamId, end)).WaitAsync(TimeSpan.FromSeconds(5));

        entries.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ReadAsync_WhenTheStreamExpires_Ends()
    {
        await Append("start");
        var reading = Collect(Reader().ReadAsync(userId, streamId, null));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1 && buffer.AppendWatchers[0].Waits > 0);

        buffer.Expire(userId, streamId);

        (await reading.WaitAsync(TimeSpan.FromSeconds(5))).Select(entry => entry.Type).Should().Equal("start");
    }

    [TestMethod]
    public async Task ReadAsync_AnotherUsersStream_IsNotRead()
    {
        await buffer.AppendAsync(Guid.NewGuid(), streamId, "start", "{}");
        var reading = Collect(Reader().ReadAsync(userId, streamId, null));
        await WaitUntil(() => buffer.AppendWatchers.Count == 1 && buffer.AppendWatchers[0].Waits > 0);

        time.Advance(ListingStreamReader.FirstEntryTimeout);
        buffer.AppendWatchers[0].Wake();

        (await reading.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeEmpty();
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
