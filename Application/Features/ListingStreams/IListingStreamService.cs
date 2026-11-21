using APCS.Application.Abstractions.Caching;
using APCS.Common.Models;

namespace APCS.Application.Features.ListingStreams;

public interface IListingStreamService
{
    /// <summary>
    /// The events of the caller's listing stream after <paramref name="afterEventId"/> (from the start without one), then
    /// the live ones until <c>end</c>.
    /// </summary>
    Result<IAsyncEnumerable<EventStreamEntry>> Attach(Guid streamId, string? afterEventId, CancellationToken cancellationToken = default);
}
