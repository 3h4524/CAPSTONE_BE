using System.Text;
using APCS.Api.Extensions;
using APCS.Application.Features.Assistant.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.UnitTests.Extensions;

[TestClass]
public sealed class ServerSentEventsResultTests
{
    private static async IAsyncEnumerable<AssistantStreamEvent> Events(params AssistantStreamEvent[] events)
    {
        foreach (var item in events)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private static (DefaultHttpContext Context, MemoryStream Body) CreateContext(CancellationToken aborted = default)
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestAborted = aborted };
        context.Response.Body = body;
        return (context, body);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_WritesEventsAsServerSentEventsWithCamelCaseJson()
    {
        var (context, body) = CreateContext();
        var id = Guid.NewGuid();
        var result = new ServerSentEventsResult(Events(AssistantStreamEvent.Conversation(id, id), AssistantStreamEvent.Delta("hi")));

        await result.ExecuteResultAsync(new ActionContext { HttpContext = context });

        var text = Encoding.UTF8.GetString(body.ToArray());
        context.Response.ContentType.Should().StartWith("text/event-stream");
        context.Response.Headers.CacheControl.ToString().Should().Contain("no-cache");
        text.Should().Contain("event: conversation\ndata: {\"conversationId\":").And.Contain("event: delta\ndata: {\"text\":\"hi\"}\n\n");
    }

    [TestMethod]
    public async Task ExecuteResultAsync_WhenRequestIsAborted_DoesNotThrow()
    {
        using var cancellation = new CancellationTokenSource();
        var (context, _) = CreateContext(cancellation.Token);
        await cancellation.CancelAsync();
        var result = new ServerSentEventsResult(Events(AssistantStreamEvent.Delta("hi")));

        var act = () => result.ExecuteResultAsync(new ActionContext { HttpContext = context });

        await act.Should().NotThrowAsync();
    }
}
