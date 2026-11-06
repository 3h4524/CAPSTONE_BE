using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin.SubscriptionPlans.Common;

/// <summary>
/// Builds every error the Admin Subscription Plans feature can return, so wording and codes stay
/// in one place (mirrors <c>SubscriptionErrors</c> for the Seller-facing feature).
/// </summary>
internal static class AdminSubscriptionPlanErrors
{
    public static Error PlanNotFound() =>
        Error.NotFound(ErrorCodes.SubscriptionPlanNotFound, "The subscription plan could not be found.");

    /// <summary>The code is not one of the plan features the backend checks.</summary>
    public static Error FeatureNotFound() =>
        Error.NotFound(ErrorCodes.PlanFeatureNotFound, "The plan feature could not be found.");

    /// <summary>A limit is always part of a plan; it is changed, not removed.</summary>
    public static Error LimitCannotBeRemoved() =>
        new(ErrorCodes.PlanFeatureInvalidChange, "A limit cannot be removed. Set it to no limit, or to a lower value, instead.", ErrorType.Validation);

    public static Error LimitOutOfRange() =>
        new(ErrorCodes.PlanFeatureInvalidChange, "A limit must be between 0 and 10,000, or empty for no limit.", ErrorType.Validation);

    /// <summary>Free's or the trial's settings cannot be saved as sent.</summary>
    public static Error InvalidSettings(string message) =>
        new(ErrorCodes.PlanFeatureInvalidChange, message, ErrorType.Validation);

    public static Error NoFeaturesToAdd() =>
        new(ErrorCodes.PlanFeatureInvalidChange, "Choose at least one feature, each only once.", ErrorType.Validation);

    /// <summary>
    /// BR193/BR194 — plan name and tier slug must each be unique.
    /// </summary>
    public static Error NameOrTierTaken() =>
        new(
            ErrorCodes.SubscriptionPlanNameOrTierTaken,
            "A plan with this name or tier slug already exists.",
            ErrorType.Validation);

    /// <summary>
    /// The Delete button only ever hard-deletes (BR200); a plan with any subscription history
    /// (active or not) must be deactivated instead — via the "Plan is active" toggle on Edit —
    /// rather than silently soft-deleted by this same action.
    /// </summary>
    public static Error HasSubscriptionHistory() =>
        new(
            ErrorCodes.SubscriptionPlanHasSubscriptionHistory,
            "This plan cannot be deleted because one or more Sellers have subscribed to it. " +
            "Deactivate it instead by turning off \"Plan is active\" on the Edit form.",
            ErrorType.Conflict);
}
