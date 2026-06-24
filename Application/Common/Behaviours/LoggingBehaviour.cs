using MediatR;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Common.Behaviours;

/// <summary>
/// Logs CQRS request execution without logging sensitive payload values.
/// </summary>
public sealed class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling request {RequestName}", requestName);

        try
        {
            var response = await next();
            logger.LogInformation("Handled request {RequestName}", requestName);

            return response;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception while handling request {RequestName}", requestName);
            throw;
        }
    }
}
