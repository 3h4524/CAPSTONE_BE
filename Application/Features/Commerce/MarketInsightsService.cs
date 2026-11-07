using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Commerce;

public interface IMarketInsightsService
{
    Task<MarketQueryMetrics?> GetQueryMetricsAsync(string query, CancellationToken cancellationToken = default);

    Task<MarketResearchJobStatus> QueueCollectionAsync(string query, CancellationToken cancellationToken = default);

    Task<MarketResearchJobStatus?> GetCollectionStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public sealed class MarketInsightsService(
    IRepository<MarketListingSnapshot> snapshots,
    IMarketResearchQueue marketResearchQueue,
    ICurrentUser currentUser) : IMarketInsightsService
{
    public async Task<MarketQueryMetrics?> GetQueryMetricsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (normalizedQuery.Length == 0)
            return null;

        var rows = await snapshots.Query()
            .Where(snapshot => snapshot.Platform == "etsy"
                && snapshot.MatchedQuery != null
                && snapshot.MatchedQuery.ToLower() == normalizedQuery.ToLower())
            .ToListAsync(cancellationToken);

        return rows.Count == 0
            ? null
            : MarketQueryMetricsCalculator.Calculate(normalizedQuery, rows);
    }

    public Task<MarketResearchJobStatus> QueueCollectionAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (normalizedQuery.Length == 0)
            throw new ArgumentException("A market insight keyword is required.", nameof(query));

        var ownerId = RequireUserId();
        return Task.FromResult(marketResearchQueue.Enqueue(
            ownerId,
            new MarketResearchRequest(CommercePlatform.Etsy, [normalizedQuery])));
    }

    public Task<MarketResearchJobStatus?> GetCollectionStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(marketResearchQueue.GetStatus(jobId, RequireUserId()));
    }

    private static string NormalizeQuery(string value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private Guid RequireUserId() => currentUser.UserId
        ?? throw new UnauthorizedAccessException("Authentication is required.");
}
