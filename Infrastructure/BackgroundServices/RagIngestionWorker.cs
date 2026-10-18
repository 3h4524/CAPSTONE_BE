using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Rag;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class RagIngestionWorker(
    IRagIngestionQueue queue,
    IServiceProvider serviceProvider,
    ILogger<RagIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RAG ingestion worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid documentId;
            try
            {
                documentId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = serviceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IRagService>()
                    .ProcessIngestAsync(documentId, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "RAG ingestion failed for document {DocumentId}.", documentId);
            }
        }
    }
}
