using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.ListingStreams.Common;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace APCS.Infrastructure.Services;

public sealed class RedisListingStreamBuffer(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
    : RedisEventStreamBuffer(redis, options), IListingStreamBuffer
{
    public Task<string> AppendAsync(Guid userId, Guid streamId, string type, string data, CancellationToken cancellationToken = default) =>
        AppendEntryAsync(StreamName(userId, streamId), type, data, cancellationToken);

    public Task<IReadOnlyList<EventStreamEntry>> ReadAfterAsync(Guid userId, Guid streamId, string? afterId, CancellationToken cancellationToken = default) =>
        ReadEntriesAfterAsync(StreamName(userId, streamId), afterId, CreateEntry, cancellationToken);

    public Task<EventStreamEntry?> ReadLastAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default) =>
        ReadLastEntryAsync(StreamName(userId, streamId), CreateEntry, cancellationToken);

    public Task<bool> ExistsAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default) =>
        StreamExistsAsync(StreamName(userId, streamId), cancellationToken);

    public async Task<IEventStreamSignal> WatchAppendsAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default) =>
        await WatchAppendedEntriesAsync(StreamName(userId, streamId), cancellationToken);

    protected override bool IsTerminal(string type) => ListingStreamEvent.IsTerminal(type);

    private static EventStreamEntry CreateEntry(string id, string type, string data) => new(id, type, data);

    private static string StreamName(Guid userId, Guid streamId) => $"listing:stream:{userId:D}:{streamId:D}";
}
