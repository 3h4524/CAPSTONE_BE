using System.Text.Json;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into ProblemDetails responses.
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request was cancelled by the client.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API exception.");

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Type = $"https://apcs/errors/{ErrorCodes.Unexpected}",
                Title = "An unexpected error occurred.",
                Detail = "An unexpected error occurred while processing the request.",
                Status = StatusCodes.Status500InternalServerError
            };

            problem.Extensions["code"] = ErrorCodes.Unexpected;
            problem.Extensions["traceId"] = context.TraceIdentifier;

            await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonOptions, context.RequestAborted);
        }
    }
}
