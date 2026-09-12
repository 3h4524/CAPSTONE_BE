using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for subscription plans.
/// </summary>
public interface IPlanRepository : IRepository<SubscriptionPlan>
{
    /// <summary>
    /// Finds every plan the Seller may compare: active plans plus the Seller's current plan even
    /// if it has since been soft-deactivated (BR91), ordered ascending by monthly price (BR93).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetComparisonPlansAsync(
        Guid? currentPlanId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an active plan that can be purchased through checkout.
    /// </summary>
    Task<SubscriptionPlan?> GetPurchasableByIdAsync(Guid planId, CancellationToken cancellationToken = default);
}
