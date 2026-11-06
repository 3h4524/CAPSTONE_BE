using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

internal sealed record MarketListingFilterResult(
    IReadOnlyList<MarketListingSnapshot> Eligible,
    int RejectedCount,
    IReadOnlyDictionary<string, int> RejectionReasons);

internal static class MarketListingFilter
{
    public static MarketListingFilterResult Apply(IReadOnlyCollection<MarketListingSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var candidates = new List<MarketListingSnapshot>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var reason = RejectionReason(snapshot);
            if (reason is null)
                candidates.Add(snapshot);
            else
                Increment(reasons, reason);
        }

        var eligible = new List<MarketListingSnapshot>(candidates.Count);
        foreach (var group in candidates.GroupBy(snapshot =>
                     new ListingSnapshotIdentity(snapshot.ExternalListingId.Trim(), snapshot.ObservedAt)))
        {
            eligible.Add(group
                .OrderByDescending(snapshot => snapshot.CollectedAt)
                .ThenByDescending(snapshot => snapshot.Id)
                .First());

            var duplicateCount = group.Count() - 1;
            if (duplicateCount > 0)
                Increment(reasons, "duplicate_snapshot", duplicateCount);
        }

        return new(
            eligible
                .OrderBy(snapshot => snapshot.ObservedAt)
                .ThenBy(snapshot => snapshot.ExternalListingId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            reasons.Values.Sum(),
            reasons);
    }

    private static string? RejectionReason(MarketListingSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Platform)) return "missing_platform";
        if (string.IsNullOrWhiteSpace(snapshot.ExternalListingId)) return "missing_listing_id";
        if (string.IsNullOrWhiteSpace(snapshot.Title)) return "missing_title";
        if (string.IsNullOrWhiteSpace(snapshot.MatchedQuery)) return "missing_query";
        if (snapshot.Price is < 0) return "invalid_price";
        if (snapshot.Favorites is < 0) return "invalid_favorites";
        if (snapshot.Views is < 0) return "invalid_views";
        return null;
    }

    private static void Increment(Dictionary<string, int> values, string key, int amount = 1) =>
        values[key] = values.GetValueOrDefault(key) + amount;

    private readonly record struct ListingSnapshotIdentity(string ListingId, DateTime ObservedAt);
}
