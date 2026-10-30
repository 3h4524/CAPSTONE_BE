using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.UsageStatistics;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Entitlements;

/// <summary>
/// Answers what a Seller's plan allows. Every feature service asks this instead of reading the
/// plan itself, so a plan rule lives in one place.
/// </summary>
public interface IPlanEntitlementService
{
    /// <summary>Gets what the Seller's current plan allows; Free when there is none.</summary>
    Task<PlanEntitlements> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Fails when the Seller's plan does not include the feature.</summary>
    Task<Result> RequireFeatureAsync(Guid userId, string featureCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fails when <paramref name="requested"/> more items would pass today's safety cap of
    /// <paramref name="capCode"/> (one of the <c>daily_*_cap</c> features). Free and the Pro trial
    /// have allowances instead, so the caps do not apply to them.
    /// </summary>
    Task<Result> RequireDailyCapacityAsync(Guid userId, string capCode, int requested = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fails when <paramref name="requested"/> more of <paramref name="allowanceCode"/> (one of
    /// <see cref="UsageAllowances"/>) would pass the Seller's Free or Pro trial allowance. A paid
    /// plan always passes.
    /// </summary>
    Task<Result> RequireAllowanceAsync(Guid userId, string allowanceCode, int requested = 1, CancellationToken cancellationToken = default);

    /// <summary>What the Seller has used of their Free or Pro trial allowances.</summary>
    Task<UsageAllowanceSummary> GetAllowancesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Fails unless the Seller has a working Gemini key, which the free Pro trial needs.</summary>
    Task<Result> RequireGeminiKeyAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PlanEntitlementService(
    ISubscriptionRepository subscriptions,
    IRepository<MockupImage> mockups,
    IRepository<PromoVideo> videos,
    IRepository<ApiUsageRecord> usageRecords,
    IApiKeyRepository apiKeys,
    IUsageAllowanceService allowances,
    IOptions<UsageAllowanceOptions> allowanceOptions,
    TimeProvider timeProvider) : IPlanEntitlementService
{
    private const string GeminiProvider = "gemini";

    /// <summary>What the Pro trial does not include: the AI video tools, billed per second of video.</summary>
    private static readonly string[] TrialExcludedFeatures = [PlanFeatures.AiVideo, PlanFeatures.StudioKeyframe];

    private readonly Dictionary<Guid, PlanEntitlements> cache = [];

    /// <inheritdoc />
    public async Task<PlanEntitlements> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(userId, out var cached)) return cached;

        var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        PlanEntitlements entitlements;
        if (subscription is null)
        {
            entitlements = PlanEntitlements.Free;
        }
        else
        {
            var isTrial = subscription.TrialEndsAt is not null;
            var features = subscription.Plan.PlanFeatures
                .Where(feature => PlanFeatures.IsKnown(feature.FeatureCode))
                .ToDictionary(
                    feature => feature.FeatureCode,
                    feature => new PlanFeatureValue(
                        (feature.IsEnabled ?? false) && !(isTrial && TrialExcludedFeatures.Contains(feature.FeatureCode)),
                        feature.LimitValue));
            entitlements = new PlanEntitlements(
                true,
                subscription.PlanId,
                subscription.Plan.Name,
                subscription.Plan.Tier,
                isTrial,
                subscription.TrialEndsAt,
                isTrial ? TrialBatchSize(subscription.Plan.MaxBatchSize) : subscription.Plan.MaxBatchSize,
                isTrial ? 1 : subscription.Plan.MaxConcurrentJobs,
                subscription.Plan.PrioritySupport == true,
                features);
        }

        cache[userId] = entitlements;
        return entitlements;
    }

    /// <inheritdoc />
    public async Task<Result> RequireFeatureAsync(Guid userId, string featureCode, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(userId, cancellationToken);
        return entitlements.IsEnabled(featureCode) ? Result.Success() : Result.Failure(PlanErrors.FeatureLocked(featureCode));
    }

    /// <inheritdoc />
    public async Task<Result> RequireDailyCapacityAsync(Guid userId, string capCode, int requested = 1, CancellationToken cancellationToken = default)
    {
        var entitlements = await GetAsync(userId, cancellationToken);
        if (entitlements.IsFree || entitlements.IsTrial) return Result.Success();

        var cap = entitlements.Limit(capCode);
        if (cap is null) return Result.Success();

        var used = await CountTodayAsync(userId, capCode, cancellationToken);
        return used + Math.Max(1, requested) <= cap
            ? Result.Success()
            : Result.Failure(PlanErrors.DailyCapReached(CapLabel(capCode)));
    }

    /// <inheritdoc />
    public async Task<Result> RequireAllowanceAsync(Guid userId, string allowanceCode, int requested = 1, CancellationToken cancellationToken = default) =>
        await allowances.RequireAsync(userId, await GetAsync(userId, cancellationToken), allowanceCode, requested, cancellationToken);

    /// <inheritdoc />
    public async Task<UsageAllowanceSummary> GetAllowancesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await allowances.GetAsync(userId, await GetAsync(userId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public async Task<Result> RequireGeminiKeyAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var keys = await apiKeys.ListOwnedAsync(userId, cancellationToken);
        // The same test design generation uses, so a trial never starts without a key that works.
        var hasKey = keys.Any(key => string.Equals(key.ServiceProvider, GeminiProvider, StringComparison.OrdinalIgnoreCase)
            && key.IsActive == true && key.LastCheckSucceeded == true
            && (!key.ExpiresAt.HasValue || key.ExpiresAt > now));
        return hasKey ? Result.Success() : Result.Failure(PlanErrors.GeminiKeyRequired());
    }

    // The trial runs a few products at a time, never more than the plan itself.
    private int TrialBatchSize(int planBatchSize)
    {
        var trial = allowanceOptions.Value.Trial.ProductsPerRun;
        return planBatchSize < 0 ? trial : Math.Min(planBatchSize, trial);
    }

    private async Task<int> CountTodayAsync(Guid userId, string capCode, CancellationToken cancellationToken)
    {
        var dayStart = timeProvider.GetUtcNow().UtcDateTime.Date;
        return capCode switch
        {
            PlanFeatures.DailyMockupCap => await mockups.Query().CountAsync(
                mockup => mockup.Product.UserId == userId && mockup.SourceType == MockupSources.Generated && mockup.CreatedAt >= dayStart,
                cancellationToken),
            PlanFeatures.DailyListingCap => await usageRecords.Query().CountAsync(
                record => record.UserId == userId && record.Feature == UsageFeatures.Listing && record.CreatedAt >= dayStart,
                cancellationToken),
            PlanFeatures.DailyVideoCap => await videos.Query().CountAsync(
                video => video.Product.UserId == userId && video.Mode == "standard" && video.CreatedAt >= dayStart,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(capCode), capCode, "Not a daily cap feature."),
        };
    }

    private static string CapLabel(string capCode) => capCode switch
    {
        PlanFeatures.DailyMockupCap => "mockups",
        PlanFeatures.DailyListingCap => "listings",
        PlanFeatures.DailyVideoCap => "videos",
        _ => capCode,
    };
}
