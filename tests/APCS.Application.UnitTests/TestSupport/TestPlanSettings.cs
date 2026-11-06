using APCS.Application.Features.Entitlements;

namespace APCS.Application.UnitTests.TestSupport;

/// <summary>
/// Free's and the trial's settings for tests: the shipped defaults, or the given allowances, and Pro
/// as the trial plan unless told otherwise.
/// </summary>
internal sealed class TestPlanSettings(UsageAllowanceOptions? allowances = null, string trialPlanTier = "pro") : IPlanSettings
{
    private readonly UsageAllowanceOptions allowances = allowances ?? new UsageAllowanceOptions();

    public Task<UsageAllowanceOptions> GetAllowancesAsync(CancellationToken cancellationToken = default) => Task.FromResult(allowances);

    public Task<string> GetTrialPlanTierAsync(CancellationToken cancellationToken = default) => Task.FromResult(trialPlanTier);

    public Task<string?> GetRecommendedPlanTierAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(PlanSettingKeys.DefaultRecommendedPlanTier);

    public Task<int> GetTrialDaysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(PlanSettingKeys.DefaultTrialDays);

    public void Invalidate()
    {
    }
}
