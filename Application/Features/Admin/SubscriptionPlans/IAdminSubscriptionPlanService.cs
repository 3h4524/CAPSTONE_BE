using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// Defines the Admin Portal's Subscription Plan use cases (ADM-03/04/05/20, 3.6.10 Delete).
/// </summary>
public interface IAdminSubscriptionPlanService
{
    /// <summary>
    /// Gets every plan tier for the Subscription Plans list (ADM-03).
    /// </summary>
    Task<Result<IReadOnlyList<AdminPlanDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single plan tier's details (ADM-04).
    /// </summary>
    Task<Result<AdminPlanDto>> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new plan tier (ADM-05 Add Subscription Plan).
    /// </summary>
    Task<Result<AdminPlanDto>> CreateAsync(
        CreatePlanRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing plan tier (3.6.9 Edit Subscription Plan).
    /// </summary>
    Task<Result<AdminPlanDto>> UpdateAsync(
        Guid planId,
        UpdatePlanRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a plan tier (3.6.10 Delete Subscription Plan). Fails with a Conflict
    /// when the plan has any subscription history — deactivate it instead via
    /// <see cref="UpdateAsync"/> (BR200).
    /// </summary>
    Task<Result> DeleteAsync(
        Guid planId,
        DeletePlanRequestDto request,
        CancellationToken cancellationToken = default);
}
