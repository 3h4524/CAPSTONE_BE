using System.Collections.Concurrent;
using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Commerce;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class MarketResearchQueueChannel(TimeProvider timeProvider) : IMarketResearchQueue
{
    private readonly Channel<MarketResearchJobWorkItem> channel = Channel.CreateUnbounded<MarketResearchJobWorkItem>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, JobState> jobs = new();

    public MarketResearchJobStatus Enqueue(Guid ownerId, MarketResearchRequest request)
    {
        var jobId = Guid.NewGuid();
        var state = new JobState(jobId, ownerId, request, timeProvider.GetUtcNow());
        jobs[jobId] = state;
        channel.Writer.TryWrite(new MarketResearchJobWorkItem(jobId, ownerId, request));
        return state.ToStatus();
    }

    public async ValueTask<MarketResearchJobWorkItem> DequeueAsync(CancellationToken cancellationToken) =>
        await channel.Reader.ReadAsync(cancellationToken);

    public MarketResearchJobStatus? GetStatus(Guid jobId, Guid ownerId) =>
        jobs.TryGetValue(jobId, out var state) && state.OwnerId == ownerId
            ? state.ToStatus()
            : null;

    public void MarkRunning(Guid jobId) =>
        Update(jobId, state => state with { Status = "running" });

    public void MarkCompleted(Guid jobId, MarketResearchResponse response) =>
        Update(jobId, state => state with
        {
            Status = "completed",
            RetrievedCount = response.RetrievedCount,
            SelectedCount = response.SelectedCount,
            CompletedAtUtc = timeProvider.GetUtcNow()
        });

    public void MarkFailed(Guid jobId, string errorMessage) =>
        Update(jobId, state => state with
        {
            Status = "failed",
            ErrorMessage = errorMessage,
            CompletedAtUtc = timeProvider.GetUtcNow()
        });

    private void Update(Guid jobId, Func<JobState, JobState> update) =>
        jobs.AddOrUpdate(
            jobId,
            _ => throw new InvalidOperationException($"Market research job {jobId} was not found."),
            (_, state) => update(state));

    private sealed record JobState(
        Guid JobId,
        Guid OwnerId,
        MarketResearchRequest Request,
        DateTimeOffset CreatedAtUtc,
        string Status = "queued",
        int RetrievedCount = 0,
        int SelectedCount = 0,
        string? ErrorMessage = null,
        DateTimeOffset? CompletedAtUtc = null)
    {
        public string Query => Request.Queries.FirstOrDefault() ?? string.Empty;

        public MarketResearchJobStatus ToStatus() => new(
            JobId,
            Query,
            Status,
            RetrievedCount,
            SelectedCount,
            ErrorMessage,
            CreatedAtUtc,
            CompletedAtUtc);
    }
}
