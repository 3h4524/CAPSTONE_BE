namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos;

/// <summary>
/// Free's and the free trial's settings, as the Admin sees and edits them.
/// </summary>
/// <param name="TrialPlanTier">The plan the trial gives; it may not be on sale any more.</param>
/// <param name="TrialPlans">The plans on sale a trial can give.</param>
/// <param name="TrialDays">How long a new trial lasts. Trials already running keep their own end.</param>
/// <param name="ActiveTrials">Trials running now.</param>
/// <param name="RecommendedPlanTier">The plan Sellers see marked Recommended; null when none is.</param>
public sealed record PlanSettingsDto(
    string TrialPlanTier,
    IReadOnlyList<TrialPlanOptionDto> TrialPlans,
    int TrialDays,
    IReadOnlyList<AllowanceSettingDto> Allowances,
    int ActiveTrials,
    DateTime? UpdatedAt,
    string? UpdatedByName,
    string? RecommendedPlanTier = null);

/// <param name="MaxBatchSize">Products per run on the plan; -1 means no limit. A trial runs no more than this.</param>
public sealed record TrialPlanOptionDto(string Tier, string Name, int MaxBatchSize);

/// <summary>One allowance of Free (each month) or the trial (its whole length).</summary>
/// <param name="Scope">"free" or "trial".</param>
/// <param name="Default">The value the backend shipped with, for "Reset to defaults".</param>
public sealed record AllowanceSettingDto(string Scope, string Code, string Label, int Limit, int Default);

/// <summary>New settings. Allowances left out keep their value.</summary>
/// <param name="TrialDays">How long new trials last, 1 to 90 days; null keeps the current length.</param>
public sealed record UpdatePlanSettingsRequestDto(string TrialPlanTier, IReadOnlyList<AllowanceValueDto>? Allowances, int? TrialDays = null);

/// <param name="Scope">"free" or "trial".</param>
public sealed record AllowanceValueDto(string Scope, string Code, int Limit);

/// <summary>The plan to mark Recommended for Sellers.</summary>
/// <param name="Tier">A plan on sale, or null to recommend none.</param>
public sealed record SetRecommendedPlanRequestDto(string? Tier);
