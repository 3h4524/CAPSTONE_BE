namespace APCS.Application.Abstractions.Caching;

public interface IListingStreamBuffer
{
    Task<string> AppendAsync(Guid userId, Guid streamId, string type, string data, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventStreamEntry>> ReadAfterAsync(Guid userId, Guid streamId, string? afterId, CancellationToken cancellationToken = default);

    Task<EventStreamEntry?> ReadLastAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default);

    Task<IEventStreamSignal> WatchAppendsAsync(Guid userId, Guid streamId, CancellationToken cancellationToken = default);
}
