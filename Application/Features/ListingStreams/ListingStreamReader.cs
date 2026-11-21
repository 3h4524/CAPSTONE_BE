using System.Runtime.CompilerServices;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.ListingStreams.Common;

namespace APCS.Application.Features.ListingStreams;

public sealed class ListingStreamReader(IListingStreamBuffer buffer, TimeProvider timeProvider)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan FirstEntryTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Follows a listing stream until its <c>end</c> event. The client may open it before the request that writes it, so
    /// an empty stream is waited for up to <see cref="FirstEntryTimeout"/>.
    /// </summary>
    public async IAsyncEnumerable<EventStreamEntry> ReadAsync(
        Guid userId,
        Guid streamId,
        string? afterId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var appended = await buffer.WatchAppendsAsync(userId, streamId, cancellationToken);
        var firstEntryDeadline = timeProvider.GetUtcNow() + FirstEntryTimeout;
        var lastId = afterId;
        var mayHaveEnded = afterId is not null;
        while (true)
        {
            var entries = await buffer.ReadAfterAsync(userId, streamId, lastId, cancellationToken);
            foreach (var entry in entries)
            {
                yield return entry;
                if (ListingStreamEvent.IsTerminal(entry.Type))
                    yield break;
                lastId = entry.Id;
            }

            if (entries.Count > 0)
            {
                mayHaveEnded = false;
                continue;
            }
            if (lastId is null)
            {
                if (timeProvider.GetUtcNow() >= firstEntryDeadline)
                    yield break;
            }
            else if (!await buffer.ExistsAsync(userId, streamId, cancellationToken))
                yield break;
            else if (mayHaveEnded)
            {
                if (await buffer.ReadLastAsync(userId, streamId, cancellationToken) is { } last && ListingStreamEvent.IsTerminal(last.Type))
                    yield break;
                mayHaveEnded = false;
            }
            await appended.WaitAsync(PollInterval, cancellationToken);
        }
    }
}
