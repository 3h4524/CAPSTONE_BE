using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.ListingStreams;

public sealed class ListingStreamPublisher(IListingStreamBuffer buffer, ICurrentUser currentUser, ILogger<ListingStreamWriter> logger)
{
    public ListingStreamWriter? Open(Guid? streamId) =>
        streamId is { } id && id != Guid.Empty && currentUser.UserId is { } userId
            ? new ListingStreamWriter(buffer, userId, id, logger)
            : null;
}
