using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Entitlements;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Commerce;

/// <summary>What a Seller's plan opens of the Market Radar.</summary>
/// <param name="Full">The whole radar (<see cref="PlanFeatures.MarketRadarFull"/>).</param>
/// <param name="AnalysesPerDay">Most AI analyses a day; null for no cap, 0 without the whole radar.</param>
/// <param name="AnalysesUsedToday">AI analyses asked for today (UTC), failed ones left out.</param>
public sealed record MarketRadarPlan(bool Full, int? AnalysesPerDay, int AnalysesUsedToday);

/// <summary>
/// Decides what of the Market Radar a Seller's plan opens. Without the whole radar a Seller sees the top
/// <see cref="MarketRadarAccess.FreeOpportunities"/> opportunities and the niche verdicts; the rest is Pro.
/// </summary>
public interface IMarketRadarAccess
{
    Task<MarketRadarPlan> GetPlanAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Fails unless the current Seller's plan opens the whole radar.</summary>
    Task<Result> RequireFullAsync(CancellationToken cancellationToken = default);

    /// <summary>Fails unless the current Seller may ask for one more AI analysis today.</summary>
    Task<Result> RequireAnalysisAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fails unless the current Seller may rank their radar now: always with the whole radar; without it when the radar
    /// was never ranked or not in the last <see cref="MarketRadarAccess.FreeRefreshInterval"/>, so a new profile gets
    /// its radar at once and the radar stays as fresh as the daily ranking.
    /// </summary>
    Task<Result> RequireRefreshAsync(CancellationToken cancellationToken = default);
}

public sealed class MarketRadarAccess(
    ICurrentUser currentUser,
    IPlanEntitlementService entitlements,
    IRepository<OpportunityAnalysisRun> runs,
    TimeProvider timeProvider,
    IRepository<SellerOpportunity>? rankings = null) : IMarketRadarAccess
{
    /// <summary>Opportunities a Seller without the whole radar sees: the best ranked ones.</summary>
    public const int FreeOpportunities = 3;

    /// <summary>How often a Seller without the whole radar may rank it by hand.</summary>
    public static readonly TimeSpan FreeRefreshInterval = TimeSpan.FromHours(24);

    public async Task<Result> RequireRefreshAsync(CancellationToken cancellationToken = default)
    {
        var full = await RequireFullAsync(cancellationToken);
        if (full.IsSuccess || rankings is null) return full;
        var userId = currentUser.UserId!.Value;
        var ranked = await rankings.Query().Where(row => row.UserId == userId)
            .Select(row => (DateTime?)row.LastEvaluatedAt).MaxAsync(cancellationToken);
        var due = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime - FreeRefreshInterval, DateTimeKind.Unspecified);
        return ranked is null || ranked <= due ? Result.Success() : full;
    }

    public async Task<MarketRadarPlan> GetPlanAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var plan = await entitlements.GetAsync(userId, cancellationToken);
        if (!plan.IsEnabled(PlanFeatures.MarketRadarFull))
            return new(false, 0, 0);
        // The cap applies on the Pro trial too: every analysis is paid for by the platform.
        var dayStart = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime.Date, DateTimeKind.Unspecified);
        var used = await runs.Query().CountAsync(
            run => run.SellerOpportunity.UserId == userId && run.RequestedAt >= dayStart && run.Status != "failed",
            cancellationToken);
        return new(true, plan.Limit(PlanFeatures.DailyOpportunityAnalysisCap), used);
    }

    public async Task<Result> RequireFullAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException("Authentication is required.");
        return await entitlements.RequireFeatureAsync(userId, PlanFeatures.MarketRadarFull, cancellationToken);
    }

    public async Task<Result> RequireAnalysisAsync(CancellationToken cancellationToken = default)
    {
        var full = await RequireFullAsync(cancellationToken);
        if (full.IsFailure) return full;
        var plan = await GetPlanAsync(currentUser.UserId!.Value, cancellationToken);
        return plan.AnalysesPerDay is { } cap && plan.AnalysesUsedToday >= cap
            ? Result.Failure(PlanErrors.DailyCapReached("opportunity analyses"))
            : Result.Success();
    }

    /// <summary>An opportunity as a Seller without the whole radar sees it: the competitors' listings left out.</summary>
    public static SellerOpportunityResponse Limited(SellerOpportunityResponse opportunity) =>
        opportunity with { Insights = opportunity.Insights is null ? null! : opportunity.Insights with { Evidence = [] } };

    /// <summary>A niche as a Seller without the whole radar sees it: the verdict and trends, not who and what to copy.</summary>
    public static SellerNicheMarketInsightResponse Limited(SellerNicheMarketInsightResponse niche) =>
        niche with
        {
            Competitors = [],
            Keywords = [],
            Signals = MarketListingSignals.Empty,
            ActionPlan = [],
            Risks = [],
        };
}
