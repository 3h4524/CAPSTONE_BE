using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>The Plan Insights tab's AI review of the paid plans.</summary>
public interface IPlanAiReviewService
{
    /// <summary>Gets the newest review that passed validation and when the next one is due.</summary>
    Task<Result<PlanAiReviewDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks the background job for a new review for the signed-in Admin.</summary>
    Task<Result<PlanAiReviewDto>> RequestAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether the job should write a review now: the last one is too old and no recent attempt failed.</summary>
    Task<bool> IsDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a review and stores it, as succeeded or failed; never throws for a model failure.</summary>
    /// <param name="triggeredBy">The Admin who asked for it; null when the job's schedule did.</param>
    Task GenerateAsync(Guid? triggeredBy, CancellationToken cancellationToken = default);
}
