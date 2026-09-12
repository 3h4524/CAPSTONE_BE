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
}
