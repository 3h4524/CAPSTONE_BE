using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists subscriptions with Entity Framework Core.
/// </summary>
public sealed class SubscriptionRepository(AppDbContext dbContext, TimeProvider timeProvider)
    : Repository<Subscription>(dbContext), ISubscriptionRepository
{
    /// <inheritdoc />
    public Task<Subscription?> GetActiveWithPlanAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var oldestUsableRenewalDate = RenewalRules.OldestUsableRenewalDate(DateOnly.FromDateTime(utcNow));
        return dbContext.Subscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Plan)
            .ThenInclude(plan => plan.PlanFeatures)
            .Include(subscription => subscription.ScheduledPlan)
            .Where(subscription => subscription.UserId == userId
                && (subscription.Status == "active" || subscription.Status == "past_due")
                && subscription.DeletedAt == null
                // A trial stops counting the moment it ends, and an unpaid plan once its grace
                // period ends, before SubscriptionRenewalJob or TrialExpiryJob marks them.
                && (subscription.TrialEndsAt == null
                    ? subscription.RenewalDate >= oldestUsableRenewalDate
                    : subscription.TrialEndsAt > utcNow))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountActiveByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.PlanId == planId
                && subscription.Status == "active"
                && subscription.DeletedAt == null
                && subscription.TrialEndsAt == null)
            .CountAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountActiveTrialsByPlanIdAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.PlanId == planId
                && subscription.Status == "active"
                && subscription.DeletedAt == null
                && subscription.TrialEndsAt > utcNow)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> HasAnySubscriptionReferenceAsync(Guid planId, CancellationToken cancellationToken = default) =>
        dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(
                subscription => subscription.PlanId == planId || subscription.ScheduledPlanId == planId,
                cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasUsedTrialAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(subscription => subscription.UserId == userId && subscription.TrialEndsAt != null, cancellationToken);
}
