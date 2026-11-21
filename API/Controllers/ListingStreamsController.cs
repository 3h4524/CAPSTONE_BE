using APCS.Api.Extensions;
using APCS.Application.Features.ListingStreams;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/listing-streams")]
public sealed class ListingStreamsController(IListingStreamService streams) : ControllerBase
{
    [HttpGet("{streamId:guid}")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Stream(
        Guid streamId,
        [FromQuery] string? after,
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
        CancellationToken cancellationToken)
    {
        var stream = streams.Attach(streamId, string.IsNullOrWhiteSpace(lastEventId) ? after : lastEventId, cancellationToken);
        return stream.IsSuccess ? new ServerSentEventsResult(stream.Value) : stream.ToActionResult(this);
    }
}
