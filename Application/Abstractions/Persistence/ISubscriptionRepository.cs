using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for subscriptions.
/// </summary>
public interface ISubscriptionRepository : IRepository<Subscription>
{
    /// <summary>
    /// Finds the user's active subscription, with its plan loaded.
    /// </summary>
    Task<Subscription?> GetActiveWithPlanAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts subscriptions currently on the given plan with an active status (BR200/BR201).
    /// </summary>
    Task<int> CountActiveByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether any subscription — active or historical — ever referenced the given plan,
    /// either as its current plan or a scheduled downgrade target. <c>subscriptions_plan_id_fkey</c>
    /// and <c>subscriptions_scheduled_plan_id_fkey</c> are both <c>ON DELETE RESTRICT</c>, so this
    /// is the actual gate for whether a plan can be hard-deleted without the database rejecting it.
    /// </summary>
    Task<bool> HasAnySubscriptionReferenceAsync(Guid planId, CancellationToken cancellationToken = default);
}
