using APCS.Domain.Entities;

namespace APCS.Application.Features.Entitlements;

/// <summary>
/// Combines a plan's features with what a subscriber keeps from before an Admin's change
/// (<see cref="SubscriptionFeatureGrant"/>), so a change never takes away what was paid for.
/// </summary>
public static class EntitlementMerge
{
    /// <summary>Why a subscriber keeps a feature: it left the plan.</summary>
    public const string FeatureRemoved = "feature_removed";

    /// <summary>Why a subscriber keeps a limit: the plan's limit went down.</summary>
    public const string LimitLowered = "limit_lowered";

    /// <summary>
    /// Gives each feature the more generous of the plan's value and the grant's. A flag is on when
    /// either is on. A limit follows <see cref="PlanEntitlements.Limit"/>: off is the least, no
    /// limit the most, and a limit with no plan row already has no limit.
    /// </summary>
    public static IReadOnlyDictionary<string, PlanFeatureValue> Merge(
        IReadOnlyDictionary<string, PlanFeatureValue> plan,
        IEnumerable<SubscriptionFeatureGrant> grants)
    {
        var merged = new Dictionary<string, PlanFeatureValue>(plan, StringComparer.Ordinal);
        foreach (var grant in grants)
        {
            var kept = new PlanFeatureValue(grant.IsEnabled, grant.LimitValue);
            if (!merged.TryGetValue(grant.FeatureCode, out var current))
            {
                // No plan row: a flag is off, so the grant wins; a limit has no limit, so nothing beats it.
                if (PlanFeatures.Find(grant.FeatureCode)?.Kind != PlanFeatureKinds.Limit)
                    merged[grant.FeatureCode] = kept;
                continue;
            }

            if (PlanFeatures.Find(grant.FeatureCode)?.Kind == PlanFeatureKinds.Limit)
            {
                if (Reach(kept) > Reach(current)) merged[grant.FeatureCode] = kept;
            }
            else if (kept.IsEnabled && !current.IsEnabled)
            {
                merged[grant.FeatureCode] = current with { IsEnabled = true };
            }
        }

        return merged;
    }

    /// <summary>How far a feature goes: off is 0, an on flag or no limit is the most.</summary>
    public static long Reach(PlanFeatureValue value) =>
        !value.IsEnabled ? 0 : value.LimitValue is { } limit ? limit : long.MaxValue;
}
