using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Rag.Common;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class RagIngestionRecoveryService(
    IServiceProvider serviceProvider,
    IRagIngestionQueue queue,
    TimeProvider timeProvider,
    ILogger<RagIngestionRecoveryService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueAsync(false, stoppingToken);
        using var timer = new PeriodicTimer(CheckEvery, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RequeueAsync(true, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RequeueAsync(bool abandonedOnly, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var threshold = timeProvider.GetUtcNow().UtcDateTime - AbandonedAfter;
            var documents = await db.RagDocuments.AsNoTracking()
                .Where(x => x.Status == RagDocumentStatuses.Queued
                    || (!abandonedOnly && x.Status == RagDocumentStatuses.Processing)
                    || (abandonedOnly && x.Status == RagDocumentStatuses.Processing && x.UpdatedAt < threshold))
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var documentId in documents)
                queue.Enqueue(documentId);
            if (documents.Count > 0)
                logger.LogInformation("Re-queued {Count} RAG document(s).", documents.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not recover RAG ingestion documents.");
        }
    }
}
