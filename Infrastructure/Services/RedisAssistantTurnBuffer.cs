using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Assistant.Common;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace APCS.Infrastructure.Services;

public sealed class RedisAssistantTurnBuffer(IConnectionMultiplexer redis, IOptions<RedisOptions> options) : IAssistantTurnBuffer
{
    public const int MaxEntries = 5000;
    public static readonly TimeSpan LiveTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FinishedTtl = TimeSpan.FromMinutes(10);

    private const int ReadBatchSize = 500;
    private const string TypeField = "type";
    private const string DataField = "data";

    public async Task<string> AppendAsync(Guid turnId, string type, string data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = redis.GetDatabase();
        var key = StreamKey(turnId);
        var id = await database.StreamAddAsync(
            key,
            [new NameValueEntry(TypeField, type), new NameValueEntry(DataField, data)],
            maxLength: MaxEntries,
            useApproximateMaxLength: true);
        await database.KeyExpireAsync(key, AssistantStreamEvent.IsTerminal(type) ? FinishedTtl : LiveTtl);
        await redis.GetSubscriber().PublishAsync(Channel(turnId, "notify"), id);
        return id.ToString();
    }

    public async Task<IReadOnlyList<AssistantTurnEntry>> ReadAfterAsync(Guid turnId, string? afterId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await redis.GetDatabase().StreamRangeAsync(
            StreamKey(turnId),
            afterId is null ? "-" : $"({afterId}",
            "+",
            ReadBatchSize);
        return entries
            .Select(entry => new AssistantTurnEntry(entry.Id.ToString(), entry[TypeField].ToString(), entry[DataField].ToString()))
            .ToArray();
    }

    public Task<bool> ExistsAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return redis.GetDatabase().KeyExistsAsync(StreamKey(turnId));
    }

    public Task<IAssistantTurnSignal> WatchAppendsAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        SubscribeAsync(Channel(turnId, "notify"), cancellationToken);

    public async Task RequestStopAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetDatabase().StringSetAsync(StopKey(turnId), "1", LiveTtl);
        await redis.GetSubscriber().PublishAsync(Channel(turnId, "stop"), "1");
    }

    public Task<bool> IsStopRequestedAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return redis.GetDatabase().KeyExistsAsync(StopKey(turnId));
    }

    public Task<IAssistantTurnSignal> WatchStopAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        SubscribeAsync(Channel(turnId, "stop"), cancellationToken);

    private string StreamKey(Guid turnId) => $"{options.Value.InstanceName}assistant:turn:{turnId:D}";

    private RedisKey StopKey(Guid turnId) => $"{StreamKey(turnId)}:stop";

    private RedisChannel Channel(Guid turnId, string name) => RedisChannel.Literal($"{StreamKey(turnId)}:{name}");

    private async Task<IAssistantTurnSignal> SubscribeAsync(RedisChannel channel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var signal = new ChannelSignal(redis.GetSubscriber(), channel);
        await signal.SubscribeAsync();
        return signal;
    }

    private sealed class ChannelSignal : IAssistantTurnSignal
    {
        private readonly ISubscriber subscriber;
        private readonly RedisChannel channel;
        private readonly SemaphoreSlim wake = new(0, 1);
        private readonly Action<RedisChannel, RedisValue> handler;

        public ChannelSignal(ISubscriber subscriber, RedisChannel channel)
        {
            this.subscriber = subscriber;
            this.channel = channel;
            handler = (_, _) => Wake();
        }

        public Task SubscribeAsync() => subscriber.SubscribeAsync(channel, handler);

        public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            await wake.WaitAsync(timeout, cancellationToken);

        public async ValueTask DisposeAsync() => await subscriber.UnsubscribeAsync(channel, handler);

        private void Wake()
        {
            try
            {
                wake.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }
}
