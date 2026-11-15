using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Gets started run groups ready (model photos, then the automatic start of the videos), each in a fresh scope that
/// acts as the group's owner, so pressing Run does not wait for the image provider.
/// </summary>
public sealed class RunGroupStartWorker(IServiceScopeFactory scopes, IRunGroupStartQueue queue,
    ILogger<RunGroupStartWorker> logger) : BackgroundService
{
    /// <summary>Groups made ready at the same time.</summary>
    public const int Concurrency = 2;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => Task.Run(() => LoopAsync(stoppingToken), stoppingToken)));

    private async Task LoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            RunGroupStartRequest request;
            try { request = await queue.DequeueAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<ActingUserContext>().ActAs(request.UserId);
                await scope.ServiceProvider.GetRequiredService<IWorkflowRunGroupService>()
                    .PrepareAsync(request.GroupId, request.AutoApprove, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Run group {GroupId} could not be made ready.", request.GroupId);
            }
            finally
            {
                queue.Done(request.GroupId);
            }
        }
    }
}
