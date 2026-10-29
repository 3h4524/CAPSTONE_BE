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

    /// <summary>
    /// Gets every plan tier for the Admin Portal (ADM-03), regardless of <c>IsActive</c>, ordered
    /// by monthly price then name (the same ascending-price order Sellers see, BR93).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAllForAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a single plan for the Admin Portal (ADM-04), regardless of <c>IsActive</c>.
    /// </summary>
    Task<SubscriptionPlan?> GetByIdForAdminAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether another plan already uses the given name or tier slug (BR193/BR194),
    /// case-insensitively. <paramref name="excludePlanId"/> excludes the plan being edited.
    /// </summary>
    Task<bool> IsNameOrTierTakenAsync(
        string name,
        string tier,
        Guid? excludePlanId,
        CancellationToken cancellationToken = default);
}
