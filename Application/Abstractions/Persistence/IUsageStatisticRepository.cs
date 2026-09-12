using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for usage statistics.
/// </summary>
public interface IUsageStatisticRepository : IRepository<UsageStatistic>
{
    /// <summary>
    /// Finds the usage-statistics row covering the given date, or <see langword="null"/> when the
    /// current billing period has not been initialized yet (SRS 3.a).
    /// </summary>
    Task<UsageStatistic?> GetCurrentPeriodAsync(
        Guid userId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}
