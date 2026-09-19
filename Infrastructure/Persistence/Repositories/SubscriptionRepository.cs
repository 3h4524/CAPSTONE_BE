using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists subscriptions with Entity Framework Core.
/// </summary>
public sealed class SubscriptionRepository(AppDbContext dbContext)
    : Repository<Subscription>(dbContext), ISubscriptionRepository
{
    /// <inheritdoc />
    public Task<Subscription?> GetActiveWithPlanAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Subscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Plan)
            .Include(subscription => subscription.ScheduledPlan)
            .Where(subscription => subscription.UserId == userId
                && subscription.Status == "active"
                && subscription.DeletedAt == null)
            .OrderByDescending(subscription => subscription.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountActiveByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.PlanId == planId
                && subscription.Status == "active"
                && subscription.DeletedAt == null)
            .CountAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasAnySubscriptionReferenceAsync(Guid planId, CancellationToken cancellationToken = default) =>
        dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(
                subscription => subscription.PlanId == planId || subscription.ScheduledPlanId == planId,
                cancellationToken);
}
