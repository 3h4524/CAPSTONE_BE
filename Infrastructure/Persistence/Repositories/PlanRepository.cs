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
            .OrderBy(plan => plan.MonthlyPriceUsd)
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
}
