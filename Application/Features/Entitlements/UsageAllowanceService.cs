using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.UsageStatistics;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Entitlements;

/// <summary>
/// Counts what a Seller on Free or the Pro trial has used and refuses what would go past their
/// allowance. Everything is counted from records the features already write, so nothing new is
/// stored: a run with a video, a design generation, design images, mockups, standard videos and
/// listings.
/// </summary>
public interface IUsageAllowanceService
{
    /// <summary>What the Seller has used and may use; no items on a paid plan.</summary>
    Task<UsageAllowanceSummary> GetAsync(Guid userId, PlanEntitlements entitlements, CancellationToken cancellationToken = default);

    /// <summary>Fails when <paramref name="requested"/> more of <paramref name="code"/> would pass the allowance.</summary>
    Task<Result> RequireAsync(
        Guid userId,
        PlanEntitlements entitlements,
        string code,
        int requested = 1,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class UsageAllowanceService(
    IRepository<WorkflowNodeRun> workflowNodes,
    IRepository<BatchJob> batchJobs,
    IRepository<ApiUsageRecord> usageRecords,
    IRepository<MockupImage> mockups,
    IRepository<PromoVideo> videos,
    IOptions<UsageAllowanceOptions> options,
    TimeProvider timeProvider) : IUsageAllowanceService
{
    private const string DesignGenerationJob = "design_generation";
    private const string VideoNode = "generate-video";
    private const string StandardVideo = "standard";

    /// <inheritdoc />
    public async Task<UsageAllowanceSummary> GetAsync(
        Guid userId,
        PlanEntitlements entitlements,
        CancellationToken cancellationToken = default)
    {
        if (Window(entitlements) is not { } window)
            return new UsageAllowanceSummary(null, null, null, []);

        var items = new List<UsageAllowanceItem>();
        foreach (var (code, limit) in Limits(entitlements))
            items.Add(new UsageAllowanceItem(code, UsageAllowances.Label(code), await CountAsync(userId, code, window, cancellationToken), limit));

        return new UsageAllowanceSummary(
            window.Scope,
            window.ResetsOn,
            entitlements.MaxMockupsPerRun >= 0 ? entitlements.MaxMockupsPerRun : null,
            items);
    }

    /// <inheritdoc />
    public async Task<Result> RequireAsync(
        Guid userId,
        PlanEntitlements entitlements,
        string code,
        int requested = 1,
        CancellationToken cancellationToken = default)
    {
        if (Window(entitlements) is not { } window) return Result.Success();
        if (!Limits(entitlements).TryGetValue(code, out var limit)) return Result.Success();

        var used = await CountAsync(userId, code, window, cancellationToken);
        return used + Math.Max(1, requested) <= limit
            ? Result.Success()
            : Result.Failure(PlanErrors.AllowanceReached(UsageAllowances.Label(code), limit, Math.Max(0, limit - used), window.Scope, window.ResetsOn));
    }

    // Free counts the calendar month (UTC); the trial its whole length; a paid plan nothing.
    private AllowanceWindow? Window(PlanEntitlements entitlements)
    {
        if (entitlements.IsFree)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var from = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddMonths(1);
            return new AllowanceWindow(UsageAllowances.ScopeFree, from, to, DateOnly.FromDateTime(to));
        }

        if (entitlements.IsTrial && entitlements.TrialEndsAt is { } endsAt)
            return new AllowanceWindow(UsageAllowances.ScopeTrial, endsAt.AddDays(-SubscriptionService.TrialDays), endsAt, DateOnly.FromDateTime(endsAt));

        return null;
    }

    private Dictionary<string, int> Limits(PlanEntitlements entitlements)
    {
        if (entitlements.IsFree)
        {
            var free = options.Value.Free;
            return new()
            {
                [UsageAllowances.FullRuns] = free.FullRuns,
                [UsageAllowances.DesignRuns] = free.DesignRuns,
                [UsageAllowances.DesignImages] = free.DesignImages,
                [UsageAllowances.Mockups] = free.Mockups,
                [UsageAllowances.Videos] = free.Videos,
                [UsageAllowances.Listings] = free.Listings,
            };
        }

        var trial = options.Value.Trial;
        return new()
        {
            [UsageAllowances.Products] = trial.Products,
            [UsageAllowances.DesignImages] = trial.DesignImages,
            [UsageAllowances.Mockups] = trial.Mockups,
            [UsageAllowances.Videos] = trial.Videos,
            [UsageAllowances.Listings] = trial.Listings,
        };
    }

    private Task<int> CountAsync(Guid userId, string code, AllowanceWindow window, CancellationToken cancellationToken) => code switch
    {
        // A run counts as a full run when it has a video step.
        UsageAllowances.FullRuns => workflowNodes.Query().CountAsync(
            node => node.NodeType == VideoNode && node.WorkflowRun.UserId == userId
                && node.WorkflowRun.CreatedAt >= window.From && node.WorkflowRun.CreatedAt < window.To,
            cancellationToken),
        UsageAllowances.DesignRuns => batchJobs.Query().CountAsync(
            job => job.UserId == userId && job.JobType == DesignGenerationJob
                && job.StartedAt >= window.From && job.StartedAt < window.To,
            cancellationToken),
        // Design images are those made by a design job; AI studio frames are not.
        UsageAllowances.DesignImages => DesignImageRecords(userId, window).CountAsync(cancellationToken),
        UsageAllowances.Products => DesignImageRecords(userId, window)
            .Where(record => record.ProductId != null)
            .Select(record => record.ProductId)
            .Distinct()
            .CountAsync(cancellationToken),
        UsageAllowances.Mockups => mockups.Query().CountAsync(
            mockup => mockup.Product.UserId == userId && mockup.SourceType == MockupSources.Generated
                && mockup.CreatedAt >= window.From && mockup.CreatedAt < window.To,
            cancellationToken),
        UsageAllowances.Videos => videos.Query().CountAsync(
            video => video.Product.UserId == userId && video.Mode == StandardVideo
                && video.CreatedAt >= window.From && video.CreatedAt < window.To,
            cancellationToken),
        UsageAllowances.Listings => usageRecords.Query().CountAsync(
            record => record.UserId == userId && record.Feature == UsageFeatures.Listing
                && record.CreatedAt >= window.From && record.CreatedAt < window.To,
            cancellationToken),
        _ => Task.FromResult(0),
    };

    private IQueryable<ApiUsageRecord> DesignImageRecords(Guid userId, AllowanceWindow window) =>
        usageRecords.Query().Where(record => record.UserId == userId && record.Feature == UsageFeatures.ImageGeneration
            && record.BatchJobId != null && record.CreatedAt >= window.From && record.CreatedAt < window.To);

    private sealed record AllowanceWindow(string Scope, DateTime From, DateTime To, DateOnly ResetsOn);
}
