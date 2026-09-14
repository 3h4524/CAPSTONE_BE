using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists usage statistics with Entity Framework Core.
/// </summary>
public sealed class UsageStatisticRepository(AppDbContext dbContext)
    : Repository<UsageStatistic>(dbContext), IUsageStatisticRepository
{
    /// <inheritdoc />
    public Task<UsageStatistic?> GetCurrentPeriodAsync(
        Guid userId,
        DateOnly asOf,
        CancellationToken cancellationToken = default) =>
        dbContext.UsageStatistics
            .AsNoTracking()
            .Where(usage => usage.UserId == userId
                && usage.BillingPeriodStart <= asOf
                && usage.BillingPeriodEnd >= asOf)
            .OrderByDescending(usage => usage.BillingPeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);
}
