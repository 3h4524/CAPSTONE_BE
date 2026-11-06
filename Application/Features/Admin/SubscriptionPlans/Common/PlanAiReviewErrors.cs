using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin.SubscriptionPlans.Common;

/// <summary>Creates expected errors returned when an Admin asks for a plan AI review.</summary>
internal static class PlanAiReviewErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized(ErrorCodes.Unauthorized, "The request is not authenticated.");

    public static Error NotConfigured() =>
        Error.Failure(ErrorCodes.PlanAiReviewNotConfigured, "No Gemini key is configured, so no AI review can be written.");

    public static Error InProgress() =>
        Error.Conflict(ErrorCodes.PlanAiReviewInProgress, "An AI review is already being written.");

    public static Error CoolingDown(DateTime availableAt) =>
        Error.Conflict(ErrorCodes.PlanAiReviewCoolingDown, $"A new AI review can be requested after {availableAt:u}.");
}
