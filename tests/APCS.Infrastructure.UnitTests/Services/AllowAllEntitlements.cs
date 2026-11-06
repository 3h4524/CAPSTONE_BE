using APCS.Application.Features.Entitlements;
using APCS.Common.Models;

namespace APCS.Infrastructure.UnitTests.Services;

/// <summary>
/// A plan that includes every feature with no limits, for tests that are not about plans.
/// </summary>
internal sealed class AllowAllEntitlements : IPlanEntitlementService
{
    private static readonly PlanEntitlements Everything = new(
        true,
        Guid.NewGuid(),
        "Pro",
        "pro",
        false,
        null,
        -1,
        -1,
        true,
        PlanFeatures.All.ToDictionary(feature => feature.Code, _ => new PlanFeatureValue(true, null)));

    public Task<PlanEntitlements> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Everything);

    public Task<Result> RequireFeatureAsync(Guid userId, string featureCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<Result> RequireDailyCapacityAsync(Guid userId, string capCode, int requested = 1, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<Result> RequireAllowanceAsync(Guid userId, string allowanceCode, int requested = 1, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<UsageAllowanceSummary> GetAllowancesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new UsageAllowanceSummary(null, null, null, []));

    public Task<Result> RequireGeminiKeyAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
