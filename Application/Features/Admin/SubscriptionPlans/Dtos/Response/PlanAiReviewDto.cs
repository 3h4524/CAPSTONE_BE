using APCS.Application.Features.Admin.SubscriptionPlans.AiReview;
using APCS.Application.Features.Revenues.Dtos;

namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;

/// <summary>The AI review of the paid plans for the Plan Insights tab, and when it changes next.</summary>
/// <param name="IsConfigured">False when no Gemini key is set, so no review can be written.</param>
/// <param name="IsGenerating">A review is being written now; the page polls until it is done.</param>
/// <param name="Latest">The newest review that passed validation; null before the first one.</param>
/// <param name="PreviousScores">Each plan's score and status in the review before <paramref name="Latest"/>, to show changes.</param>
/// <param name="NextRefreshAt">When the job writes the next review on its own.</param>
/// <param name="LastFailedAt">The newest failed attempt after <paramref name="Latest"/>; null when there is none.</param>
/// <param name="RegenerateAvailableAt">When an Admin may ask for a review again; null when they may now.</param>
public sealed record PlanAiReviewDto(
    bool IsConfigured,
    bool IsGenerating,
    PlanAiReviewSnapshotDto? Latest,
    IReadOnlyList<PlanAiScoreDto> PreviousScores,
    DateTime? NextRefreshAt,
    DateTime? LastFailedAt,
    DateTime? RegenerateAvailableAt);

/// <param name="Plans">The plans and features the review names, by key, with their display names.</param>
/// <param name="MrrBridges">
/// MRR from the start to the end of the review's period, from the figures the model was given: all plans
/// together (plan name null) and then each plan. The page shows these so the text never has to.
/// </param>
public sealed record PlanAiReviewSnapshotDto(
    Guid Id,
    DateTime GeneratedAt,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Model,
    IReadOnlyList<PlanAiReviewPlanNameDto> Plans,
    PlanAiReviewOutput Review,
    IReadOnlyList<MrrBridgeDto> MrrBridges);

public sealed record PlanAiReviewPlanNameDto(string Key, string Name, IReadOnlyList<PlanAiReviewFeatureNameDto> Features);

public sealed record PlanAiReviewFeatureNameDto(string Key, string Name);

public sealed record PlanAiScoreDto(string Plan, string Status, int? Score);
