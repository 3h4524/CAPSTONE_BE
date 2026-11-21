using APCS.Application.Abstractions.Caching;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace APCS.Infrastructure.Services;

public abstract class RedisEventStreamBuffer(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
{
    public const int MaxEntries = 5000;
    public static readonly TimeSpan LiveTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FinishedTtl = TimeSpan.FromMinutes(10);

    private const int ReadBatchSize = 500;
    private const string TypeField = "type";
    private const string DataField = "data";

    protected IConnectionMultiplexer Redis => redis;

    protected abstract bool IsTerminal(string type);

    protected string StreamKey(string streamName) => $"{options.Value.InstanceName}{streamName}";

    protected RedisChannel Channel(string streamName, string name) => RedisChannel.Literal($"{StreamKey(streamName)}:{name}");

    protected async Task<string> AppendEntryAsync(string streamName, string type, string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = redis.GetDatabase();
        var key = StreamKey(streamName);
        var id = await database.StreamAddAsync(
            key,
            [new NameValueEntry(TypeField, type), new NameValueEntry(DataField, data)],
            maxLength: MaxEntries,
            useApproximateMaxLength: true);
        await database.KeyExpireAsync(key, IsTerminal(type) ? FinishedTtl : LiveTtl);
        await redis.GetSubscriber().PublishAsync(Channel(streamName, "notify"), id);
        return id.ToString();
    }

    protected async Task<IReadOnlyList<TEntry>> ReadEntriesAfterAsync<TEntry>(
        string streamName,
        string? afterId,
        Func<string, string, string, TEntry> create,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await redis.GetDatabase().StreamRangeAsync(
            StreamKey(streamName),
            afterId is null ? "-" : $"({afterId}",
            "+",
            ReadBatchSize);
        return entries.Select(entry => ToEntry(entry, create)).ToArray();
    }

    protected async Task<TEntry?> ReadLastEntryAsync<TEntry>(
        string streamName,
        Func<string, string, string, TEntry> create,
        CancellationToken cancellationToken)
        where TEntry : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await redis.GetDatabase().StreamRangeAsync(StreamKey(streamName), "-", "+", 1, Order.Descending);
        return entries.Length == 0 ? null : ToEntry(entries[0], create);
    }

    protected Task<bool> StreamExistsAsync(string streamName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return redis.GetDatabase().KeyExistsAsync(StreamKey(streamName));
    }

    protected Task<IAssistantTurnSignal> WatchAppendedEntriesAsync(string streamName, CancellationToken cancellationToken) =>
        SubscribeAsync(Channel(streamName, "notify"), cancellationToken);

    protected async Task<IAssistantTurnSignal> SubscribeAsync(RedisChannel channel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var signal = new ChannelSignal(redis.GetSubscriber(), channel);
        await signal.SubscribeAsync();
        return signal;
    }

    private static TEntry ToEntry<TEntry>(StreamEntry entry, Func<string, string, string, TEntry> create) =>
        create(entry.Id.ToString(), entry[TypeField].ToString(), entry[DataField].ToString());

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
