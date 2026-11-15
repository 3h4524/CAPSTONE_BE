using System.Text;
using APCS.Api.Extensions;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Assistant.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.UnitTests.Extensions;

[TestClass]
public sealed class ServerSentEventsResultTests
{
    private static async IAsyncEnumerable<AssistantTurnEntry> Events(params AssistantTurnEntry[] events)
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
    public async Task ExecuteResultAsync_WritesEventsWithTheirStreamIdsAndCamelCaseJson()
    {
        var (context, body) = CreateContext();
        var id = Guid.NewGuid();
        var conversation = AssistantStreamEvent.Conversation(id, id, id);
        var delta = AssistantStreamEvent.Delta("hi");
        var result = new ServerSentEventsResult(Events(
            new AssistantTurnEntry("1-0", conversation.Type, conversation.DataJson()),
            new AssistantTurnEntry("2-0", delta.Type, delta.DataJson())));

        await result.ExecuteResultAsync(new ActionContext { HttpContext = context });

        var text = Encoding.UTF8.GetString(body.ToArray());
        context.Response.ContentType.Should().StartWith("text/event-stream");
        context.Response.Headers.CacheControl.ToString().Should().Contain("no-cache");
        text.Should().Contain("id: 1-0\nevent: conversation\ndata: {\"conversationId\":").And.Contain("\"assistantMessageId\":")
            .And.Contain("id: 2-0\nevent: delta\ndata: {\"text\":\"hi\"}\n\n");
    }

    [TestMethod]
    public async Task ExecuteResultAsync_WhenRequestIsAborted_DoesNotThrow()
    {
        using var cancellation = new CancellationTokenSource();
        var (context, _) = CreateContext(cancellation.Token);
        await cancellation.CancelAsync();
        var result = new ServerSentEventsResult(Events(new AssistantTurnEntry("1-0", "delta", """{"text":"hi"}""")));

        var act = () => result.ExecuteResultAsync(new ActionContext { HttpContext = context });

        await act.Should().NotThrowAsync();
    }
}
