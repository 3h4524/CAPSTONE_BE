using APCS.Application.Abstractions.Caching;
using APCS.Application.UnitTests.Features.Assistant;

namespace APCS.Application.UnitTests.Features.ListingStreams;

internal sealed class FakeListingStreamBuffer : IListingStreamBuffer
{
    private readonly object gate = new();
    private readonly Dictionary<(Guid UserId, Guid StreamId), List<EventStreamEntry>> streams = [];
    private long sequence;

    public List<FakeTurnSignal> AppendWatchers { get; } = [];

    public Exception? AppendFailure { get; set; }

    public IReadOnlyList<EventStreamEntry> Entries(Guid userId, Guid streamId)
    {
        lock (gate)
            return streams.TryGetValue((userId, streamId), out var entries) ? entries.ToArray() : [];
    }

    public void Expire(Guid userId, Guid streamId)
    {
        lock (gate)
            streams.Remove((userId, streamId));
    }

    public Task<string> AppendAsync(Guid userId, Guid streamId, string type, string data, CancellationToken cancellationToken = default)
    {
        if (AppendFailure is not null)
            throw AppendFailure;
        EventStreamEntry entry;
        FakeTurnSignal[] watchers;
        lock (gate)
        {
            entry = new EventStreamEntry($"{++sequence}-0", type, data);
            if (!streams.TryGetValue((userId, streamId), out var entries))
                streams[(userId, streamId)] = entries = [];
            entries.Add(entry);
            watchers = AppendWatchers.ToArray();
        }

        foreach (var watcher in watchers)
            watcher.Wake();
        return Task.FromResult(entry.Id);
    }

    public Task<IReadOnlyList<EventStreamEntry>> ReadAfterAsync(Guid userId, Guid streamId, string? afterId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var after = afterId is null ? 0 : long.Parse(afterId.Split('-')[0]);
        lock (gate)
        {
            IReadOnlyList<EventStreamEntry> entries = streams.TryGetValue((userId, streamId), out var list)
                ? list.Where(entry => long.Parse(entry.Id.Split('-')[0]) > after).ToArray()
                : [];
            return Task.FromResult(entries);
        }
    }

    public Task<EventStreamEntry?> ReadLastAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return Task.FromResult(streams.TryGetValue((userId, streamId), out var list) ? list.LastOrDefault() : null);
    }

    public Task<bool> ExistsAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return Task.FromResult(streams.ContainsKey((userId, streamId)));
    }

    public Task<IEventStreamSignal> WatchAppendsAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default)
    {
        var signal = new FakeTurnSignal();
        lock (gate)
            AppendWatchers.Add(signal);
        return Task.FromResult<IEventStreamSignal>(signal);
    }
}
