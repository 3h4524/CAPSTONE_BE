using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists subscription plans with Entity Framework Core.
/// </summary>
public sealed class PlanRepository(AppDbContext dbContext)
    : Repository<SubscriptionPlan>(dbContext), IPlanRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<SubscriptionPlan>> GetComparisonPlansAsync(
        Guid? currentPlanId,
        CancellationToken cancellationToken = default) =>
        QueryComparisonPlansAsync(currentPlanId, cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlan>> QueryComparisonPlansAsync(
        Guid? currentPlanId,
        CancellationToken cancellationToken)
    {
        var plans = await dbContext.SubscriptionPlans
            .AsNoTracking()
            .Include(plan => plan.PlanFeatures)
            .Where(plan => plan.IsActive == true || plan.Id == currentPlanId)
            // The Admin's order first; plans never ordered (0 or empty) follow by price.
            .OrderBy(plan => plan.SortOrder > 0 ? plan.SortOrder : int.MaxValue)
            .ThenBy(plan => plan.MonthlyPriceUsd)
            .ToListAsync(cancellationToken);

        return plans;
    }

    /// <inheritdoc />
    public Task<SubscriptionPlan?> GetPurchasableByIdAsync(
        Guid planId,
        CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.Id == planId && plan.IsActive == true)
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<SubscriptionPlan?> GetPurchasableByTierAsync(
        string tier,
        CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.Tier == tier && plan.IsActive == true)
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SubscriptionPlan>> GetAllForAdminAsync(CancellationToken cancellationToken = default) =>
        QueryAllForAdminAsync(cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlan>> QueryAllForAdminAsync(CancellationToken cancellationToken)
    {
        var plans = await dbContext.SubscriptionPlans
            .AsNoTracking()
            .Include(plan => plan.PlanFeatures)
            .OrderBy(plan => plan.SortOrder > 0 ? plan.SortOrder : int.MaxValue)
            .ThenBy(plan => plan.MonthlyPriceUsd)
            .ThenBy(plan => plan.Name)
            .ToListAsync(cancellationToken);

        return plans;
    }

    /// <inheritdoc />
    public Task<SubscriptionPlan?> GetByIdForAdminAsync(
        Guid planId,
        CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlans
            .AsNoTracking()
            .Include(plan => plan.PlanFeatures)
            .SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsNameOrTierTakenAsync(
        string name,
        string tier,
        Guid? excludePlanId,
        CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => excludePlanId == null || plan.Id != excludePlanId)
            .AnyAsync(
                plan => plan.Name.ToLower() == name.ToLower() || plan.Tier.ToLower() == tier.ToLower(),
                cancellationToken);
}
