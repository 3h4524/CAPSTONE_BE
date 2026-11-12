using APCS.Application.Abstractions.Caching;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Extensions;

public sealed class ServerSentEventsResult(IAsyncEnumerable<AssistantTurnEntry> events) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var httpContext = context.HttpContext;
        var response = httpContext.Response;
        var cancellationToken = httpContext.RequestAborted;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-store";
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await foreach (var item in events.WithCancellation(cancellationToken))
            {
                await response.WriteAsync($"id: {item.Id}\nevent: {item.Type}\ndata: {item.Data}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }
}
