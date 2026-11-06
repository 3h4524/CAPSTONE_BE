using APCS.Application.Features.Commerce;

namespace APCS.Application.Abstractions.BackgroundJobs;

public interface IMarketResearchQueue
{
    MarketResearchJobStatus Enqueue(Guid ownerId, MarketResearchRequest request);

    ValueTask<MarketResearchJobWorkItem> DequeueAsync(CancellationToken cancellationToken);

    MarketResearchJobStatus? GetStatus(Guid jobId, Guid ownerId);

    void MarkRunning(Guid jobId);

    void MarkCompleted(Guid jobId, MarketResearchResponse response);

    void MarkFailed(Guid jobId, string errorMessage);
}

public sealed record MarketResearchJobWorkItem(
    Guid JobId,
    Guid OwnerId,
    MarketResearchRequest Request);

public sealed record MarketResearchJobStatus(
    Guid JobId,
    string Query,
    string Status,
    int RetrievedCount,
    int SelectedCount,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
