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
}
