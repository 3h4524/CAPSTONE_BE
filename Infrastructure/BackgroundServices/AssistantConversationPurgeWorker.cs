using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class AssistantConversationPurgeWorker(
    IAssistantConversationPurgeQueue queue,
    IServiceProvider serviceProvider,
    ILogger<AssistantConversationPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        queue.RequestPurge();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queue.WaitForRequestAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = serviceProvider.CreateScope();
                var purged = await scope.ServiceProvider.GetRequiredService<IAssistantConversationRepository>()
                    .PurgeDeletedAsync(stoppingToken);
                if (purged > 0)
                    logger.LogInformation("Purged {Count} deleted assistant conversations.", purged);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Purging deleted assistant conversations failed.");
            }
        }
    }
}
