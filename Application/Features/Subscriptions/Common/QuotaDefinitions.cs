using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// Builds the usage cards on the Subscription Page from the current period's usage.
/// </summary>
/// <remarks>
/// Plans differ by feature and have no monthly quotas, so each card reports what was used with a
/// limit of 0, which the client shows as "no limit". Data-driven so a new usage figure is a single
/// entry, not a new code path.
/// </remarks>
public static class QuotaDefinitions
{
    private sealed record Definition(
        string Code,
        string Label,
        string Unit,
        Func<UsageStatistic?, decimal> Used);

    private static readonly IReadOnlyList<Definition> Definitions =
    [
        new Definition(
            "image_generation",
            "Image generation",
            "images",
            usage => usage?.ImagesGenerated ?? 0)
    ];

    /// <summary>
    /// Builds one <see cref="UsageQuotaDto"/> per definition for the (possibly uninitialized)
    /// current-period usage.
    /// </summary>
    public static IReadOnlyList<UsageQuotaDto> Build(UsageStatistic? usage) =>
        Definitions
            .Select(definition => new UsageQuotaDto(
                definition.Code,
                definition.Label,
                definition.Used(usage),
                0m,
                definition.Unit,
                0m,
                false))
            .ToList();
}
