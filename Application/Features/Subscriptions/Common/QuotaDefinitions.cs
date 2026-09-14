using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// Builds the D2 usage-quota cards from real schema fields (Image Generation, API Calls,
/// Storage) instead of the SRS's literal "credits/connected stores/workspace seats" wording,
/// which has no counterpart anywhere in the database. Data-driven so a future quota is a single
/// entry, not a new code path.
/// </summary>
public static class QuotaDefinitions
{
    private sealed record Definition(
        string Code,
        string Label,
        string Unit,
        Func<SubscriptionPlan, decimal> Limit,
        Func<UsageStatistic?, decimal> Used);

    private static readonly IReadOnlyList<Definition> Definitions =
    [
        new Definition(
            "image_generation",
            "Image generation",
            "images",
            plan => plan.ImageGenerationQuota,
            usage => usage?.ImagesGenerated ?? 0),
        new Definition(
            "api_calls",
            "API calls",
            "calls",
            plan => plan.ApiCallQuota,
            usage => usage?.TotalApiCalls ?? 0),
        new Definition(
            "storage",
            "Storage",
            "GB",
            plan => plan.StorageQuotaGb,
            usage => usage?.StorageUsedGb ?? 0)
    ];

    /// <summary>
    /// Builds one <see cref="UsageQuotaDto"/> per definition for the given plan and (possibly
    /// uninitialized) current-period usage.
    /// </summary>
    public static IReadOnlyList<UsageQuotaDto> Build(SubscriptionPlan plan, UsageStatistic? usage)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return Definitions
            .Select(definition =>
            {
                var limit = definition.Limit(plan);
                var used = definition.Used(usage);
                var percentUsed = limit <= 0
                    ? 0m
                    : Math.Min(100m, Math.Round(used / limit * 100m, 0, MidpointRounding.AwayFromZero));

                return new UsageQuotaDto(
                    definition.Code,
                    definition.Label,
                    used,
                    limit,
                    definition.Unit,
                    percentUsed,
                    percentUsed >= 100m);
            })
            .ToList();
    }
}
