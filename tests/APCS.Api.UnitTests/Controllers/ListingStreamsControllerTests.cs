using System.Text;
using APCS.Api.Controllers;
using APCS.Api.Extensions;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.ListingStreams;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class ListingStreamsControllerTests
{
    private readonly Mock<IListingStreamService> service = new(MockBehavior.Strict);

    private ListingStreamsController Controller(HttpContext? context = null) =>
        new(service.Object) { ControllerContext = new ControllerContext { HttpContext = context ?? new DefaultHttpContext() } };

    private static async IAsyncEnumerable<EventStreamEntry> Events(params EventStreamEntry[] entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            await Task.Yield();
        }
    }

    [TestMethod]
    public async Task Stream_WritesTheListingEventsAsServerSentEvents()
    {
        var streamId = Guid.NewGuid();
        service.Setup(s => s.Attach(streamId, null, It.IsAny<CancellationToken>()))
            .Returns(Result.Success(Events(new("1-0", "delta", """{"productId":"p","text":"{"}"""), new("2-0", "end", "{}"))));
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;

        var result = Controller(context).Stream(streamId, null, null, default);
        await result.ExecuteResultAsync(new ActionContext { HttpContext = context });

        result.Should().BeOfType<ServerSentEventsResult>();
        context.Response.ContentType.Should().StartWith("text/event-stream");
        Encoding.UTF8.GetString(body.ToArray()).Should()
            .Be("id: 1-0\nevent: delta\ndata: {\"productId\":\"p\",\"text\":\"{\"}\n\nid: 2-0\nevent: end\ndata: {}\n\n");
    }

    [TestMethod]
    public void Stream_PrefersTheLastEventIdHeaderOverTheQuery()
    {
        using var cancellation = new CancellationTokenSource();
        var streamId = Guid.NewGuid();
        service.Setup(s => s.Attach(streamId, "5-0", cancellation.Token)).Returns(Result.Success(Events()));

        var result = Controller().Stream(streamId, "3-0", "5-0", cancellation.Token);

        result.Should().BeOfType<ServerSentEventsResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public void Stream_WithoutTheHeader_UsesTheAfterQuery()
    {
        var streamId = Guid.NewGuid();
        service.Setup(s => s.Attach(streamId, "3-0", It.IsAny<CancellationToken>())).Returns(Result.Success(Events()));

        var result = Controller().Stream(streamId, "3-0", null, default);

        result.Should().BeOfType<ServerSentEventsResult>();
        service.VerifyAll();
    }

    [TestMethod]
    public void Stream_WithAnInvalidEventId_ReturnsProblem400()
    {
        service.Setup(s => s.Attach(It.IsAny<Guid>(), "nope", It.IsAny<CancellationToken>()))
            .Returns(Result.Failure<IAsyncEnumerable<EventStreamEntry>>(Error.Validation("The last event id is not valid.")));

        var result = Controller().Stream(Guid.NewGuid(), "nope", null, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public void Controller_IsForSellersOnly()
    {
        typeof(ListingStreamsController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Should().ContainSingle().Which.As<AuthorizeAttribute>().Roles.Should().Be(AuthConstants.UserRole);
    }
}
