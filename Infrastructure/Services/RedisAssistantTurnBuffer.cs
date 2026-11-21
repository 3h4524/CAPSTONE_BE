using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Assistant.Common;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace APCS.Infrastructure.Services;

public sealed class RedisAssistantTurnBuffer(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
    : RedisEventStreamBuffer(redis, options), IAssistantTurnBuffer
{
    public Task<string> AppendAsync(Guid turnId, string type, string data, CancellationToken cancellationToken = default) =>
        AppendEntryAsync(StreamName(turnId), type, data, cancellationToken);

    public Task<IReadOnlyList<AssistantTurnEntry>> ReadAfterAsync(Guid turnId, string? afterId, CancellationToken cancellationToken = default) =>
        ReadEntriesAfterAsync(StreamName(turnId), afterId, (id, type, data) => new AssistantTurnEntry(id, type, data), cancellationToken);

    public Task<bool> ExistsAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        StreamExistsAsync(StreamName(turnId), cancellationToken);

    public Task<IAssistantTurnSignal> WatchAppendsAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        WatchAppendedEntriesAsync(StreamName(turnId), cancellationToken);

    public async Task RequestStopAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Redis.GetDatabase().StringSetAsync(StopKey(turnId), "1", LiveTtl);
        await Redis.GetSubscriber().PublishAsync(Channel(StreamName(turnId), "stop"), "1");
    }

    public Task<bool> IsStopRequestedAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Redis.GetDatabase().KeyExistsAsync(StopKey(turnId));
    }

    public Task<IAssistantTurnSignal> WatchStopAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        SubscribeAsync(Channel(StreamName(turnId), "stop"), cancellationToken);

    protected override bool IsTerminal(string type) => AssistantStreamEvent.IsTerminal(type);

    private static string StreamName(Guid turnId) => $"assistant:turn:{turnId:D}";

    private RedisKey StopKey(Guid turnId) => $"{StreamKey(StreamName(turnId))}:stop";
}
