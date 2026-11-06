using APCS.Application.Features.Entitlements;
using APCS.Common.Models;

namespace APCS.Application.UnitTests.TestSupport;

/// <summary>
/// Plan entitlements for tests that are not about plans: either a plan that includes everything
/// with no limits, or a fixed set of features and limits.
/// </summary>
internal sealed class TestEntitlements(PlanEntitlements entitlements) : IPlanEntitlementService
{
    /// <summary>A plan with every feature on and no limits.</summary>
    public static TestEntitlements AllowAll() =>
        new(new PlanEntitlements(
            true,
            Guid.NewGuid(),
            "Pro",
            "pro",
            false,
            null,
            -1,
            -1,
            true,
            PlanFeatures.All.ToDictionary(
                feature => feature.Code,
                feature => new PlanFeatureValue(true, null))));

    /// <summary>A Seller with no plan in effect: Free.</summary>
    public static TestEntitlements Free() => new(PlanEntitlements.Free);

    /// <summary>A plan with the given features on, limits and batch settings.</summary>
    public static TestEntitlements Plan(
        int maxBatchSize = -1,
        int maxConcurrentJobs = -1,
        IReadOnlyDictionary<string, PlanFeatureValue>? features = null) =>
        new(new PlanEntitlements(
            true,
            Guid.NewGuid(),
            "Starter",
            "starter",
            false,
            null,
            maxBatchSize,
            maxConcurrentJobs,
            false,
            features ?? new Dictionary<string, PlanFeatureValue>()));

    /// <summary>The daily cap results to return, keyed by cap code; caps not listed pass.</summary>
    public Dictionary<string, bool> CapsReached { get; } = [];

    /// <summary>The allowances that are used up, by code; allowances not listed pass.</summary>
    public HashSet<string> AllowancesUsedUp { get; } = [];

    /// <summary>Every allowance asked for, with how many were requested.</summary>
    public List<(string Code, int Requested)> AllowancesRequested { get; } = [];

    /// <summary>Whether the Seller has a working Gemini key.</summary>
    public bool HasGeminiKey { get; set; } = true;

    public Task<PlanEntitlements> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(entitlements);

    public Task<Result> RequireFeatureAsync(Guid userId, string featureCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(entitlements.IsEnabled(featureCode)
            ? Result.Success()
            : Result.Failure(PlanErrors.FeatureLocked(featureCode)));

    public Task<Result> RequireDailyCapacityAsync(Guid userId, string capCode, int requested = 1, CancellationToken cancellationToken = default) =>
        Task.FromResult(CapsReached.GetValueOrDefault(capCode)
            ? Result.Failure(PlanErrors.DailyCapReached(capCode))
            : Result.Success());

    public Task<Result> RequireAllowanceAsync(Guid userId, string allowanceCode, int requested = 1, CancellationToken cancellationToken = default)
    {
        AllowancesRequested.Add((allowanceCode, requested));
        return Task.FromResult(AllowancesUsedUp.Contains(allowanceCode)
            ? Result.Failure(PlanErrors.AllowanceReached(UsageAllowances.Label(allowanceCode), 2, 0, UsageAllowances.ScopeFree, new DateOnly(2026, 12, 1)))
            : Result.Success());
    }

    public Task<UsageAllowanceSummary> GetAllowancesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new UsageAllowanceSummary(null, null, null, []));

    public Task<Result> RequireGeminiKeyAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasGeminiKey ? Result.Success() : Result.Failure(PlanErrors.GeminiKeyRequired()));
}
