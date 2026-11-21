using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Common.Models;

namespace APCS.Application.Features.ListingStreams;

public sealed class ListingStreamService(ICurrentUser currentUser, ListingStreamReader reader) : IListingStreamService
{
    public Result<IAsyncEnumerable<EventStreamEntry>> Attach(Guid streamId, string? afterEventId, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<IAsyncEnumerable<EventStreamEntry>>(Error.Unauthorized("ListingStream.Unauthenticated", "The request is not authenticated."));
        var afterId = string.IsNullOrWhiteSpace(afterEventId) ? null : afterEventId.Trim();
        if (afterId is not null && !EventStreamEntry.IsEntryId(afterId))
            return Result.Failure<IAsyncEnumerable<EventStreamEntry>>(Error.Validation("The last event id is not valid."));

        return Result.Success(reader.ReadAsync(userId, streamId, afterId, cancellationToken));
    }
}
