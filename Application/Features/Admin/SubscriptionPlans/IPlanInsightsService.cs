using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// Builds the Admin Portal's Plan Insights tab.
/// </summary>
public interface IPlanInsightsService
{
    /// <summary>Gets plan insights for the last <see cref="PlanInsightsService.PeriodDays"/> days.</summary>
    Task<Result<PlanInsightsDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Sellers who used a plan feature between <paramref name="from"/> and
    /// <paramref name="to"/>, or null when Plan Insights has no measure for that feature.
    /// </summary>
    Task<IReadOnlySet<Guid>?> UsersOfFeatureAsync(string featureCode, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
