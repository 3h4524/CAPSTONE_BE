namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;

/// <summary>A plan feature's setting: whether the plan has it, and a limit's value (null for no limit).</summary>
public sealed record PlanFeatureSettingDto(bool IsEnabled, int? LimitValue);

/// <summary>
/// What one change did to a plan feature, so the Admin can undo it by sending <paramref name="Before"/> back.
/// </summary>
/// <param name="KeptBy">Subscribers who keep the old setting until their period ends.</param>
/// <param name="KeptUntil">The last of those periods to end; null when nobody keeps it.</param>
public sealed record PlanFeatureChangeDto(
    string Code,
    string Label,
    string Kind,
    PlanFeatureSettingDto Before,
    PlanFeatureSettingDto After,
    int KeptBy,
    DateOnly? KeptUntil);

/// <summary>Every feature change made by one request to a plan.</summary>
public sealed record PlanFeatureChangesDto(Guid PlanId, string PlanName, IReadOnlyList<PlanFeatureChangeDto> Changes);

/// <summary>
/// Who a change to a plan feature would reach, shown before the Admin confirms it.
/// </summary>
/// <param name="Subscribers">Paying subscribers on the plan.</param>
/// <param name="Trials">Free trials on the plan.</param>
/// <param name="IsReduction">True when the change takes something away; only then does anyone keep the old setting.</param>
/// <param name="KeptBy">Subscribers who would keep the old setting until their period ends.</param>
/// <param name="KeptUntil">The last of those periods to end.</param>
/// <param name="UsedRecently">Plan subscribers who used the feature in the last <paramref name="UsageDays"/> days; null when it is not measured.</param>
/// <param name="PaidForBy">Of <paramref name="KeptBy"/>, those whose period began with the plan giving it: they keep it until <paramref name="PaidForUntil"/> at the latest.</param>
/// <param name="NoticeBy">Of <paramref name="KeptBy"/>, those given it after their period began: they keep it for <paramref name="NoticeDays"/> days, until <paramref name="NoticeUntil"/> at the latest.</param>
/// <param name="GivenRecently">True when the plan started giving it less than a day ago: nobody keeps it, as with a mistake undone.</param>
public sealed record PlanFeatureImpactDto(
    string Code,
    string Label,
    PlanFeatureSettingDto Current,
    PlanFeatureSettingDto Proposed,
    int Subscribers,
    int Trials,
    bool IsReduction,
    int KeptBy,
    DateOnly? KeptUntil,
    int? UsedRecently,
    int UsageDays,
    int PaidForBy,
    DateOnly? PaidForUntil,
    int NoticeBy,
    DateOnly? NoticeUntil,
    bool GivenRecently,
    int NoticeDays);
