using System.Text.Json;
using APCS.Application.Features.Assistant.Common;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Extensions;

public sealed class ServerSentEventsResult(IAsyncEnumerable<AssistantStreamEvent> events) : IActionResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
                var data = JsonSerializer.Serialize(item.Data, item.Data.GetType(), JsonOptions);
                await response.WriteAsync($"event: {item.Type}\ndata: {data}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }
}
