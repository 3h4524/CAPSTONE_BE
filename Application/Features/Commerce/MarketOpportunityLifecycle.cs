namespace APCS.Application.Features.Commerce;

/// <summary>
/// How a market opportunity ages and how sure the radar is of it. An opportunity is kept fresh by its listings showing
/// up again in the daily collection; one the marketplace no longer shows goes stale, then is archived, and comes back
/// to active as soon as it is observed again.
/// </summary>
public static class MarketOpportunityLifecycle
{
    public const string Active = "active";
    public const string Stale = "stale";
    public const string Archived = "archived";

    /// <summary>Days without an observation before an opportunity is stale: two weeks of daily collection missed it.</summary>
    public const int StaleAfterDays = 14;

    /// <summary>Days without an observation before an opportunity is archived and leaves the radar.</summary>
    public const int ArchiveAfterDays = 45;

    /// <summary>The weight of a new observation in the market score; the rest is the score so far.</summary>
    public const decimal ObservationWeight = 0.5m;

    /// <summary>
    /// The status an opportunity last observed at <paramref name="lastObservedAt"/> has at <paramref name="nowUtc"/>.
    /// Only ageing is decided here; an observation makes it active again where it is recorded.
    /// </summary>
    public static string StatusFor(string current, DateTime lastObservedAt, DateTime nowUtc)
    {
        var ageDays = (nowUtc - lastObservedAt).TotalDays;
        if (ageDays >= ArchiveAfterDays) return Archived;
        if (ageDays >= StaleAfterDays) return current == Archived ? Archived : Stale;
        return current;
    }

    /// <summary>
    /// The market score after a new observation: an average leaning on the latest one, so a niche that cools down
    /// loses score instead of keeping its best day forever.
    /// </summary>
    public static decimal BlendScore(decimal current, double observed) =>
        Math.Round(Math.Clamp(current * (1 - ObservationWeight) + (decimal)observed * ObservationWeight, 0m, 100m), 2);

    /// <summary>
    /// How sure the radar is of an opportunity, from 0 to 1: more listings, more days observed and more shops behind
    /// it raise it; evidence that is only synthetic (demo data) keeps it low. Five listings, a week of observations and
    /// three shops count as full.
    /// </summary>
    public static decimal Confidence(int listings, int observedDays, int sellers, int realEvidence, int syntheticEvidence)
    {
        if (realEvidence + syntheticEvidence == 0) return 0.2m;
        var coverage = 0.4 * Math.Min(1, listings / 5d) + 0.4 * Math.Min(1, observedDays / 7d) + 0.2 * Math.Min(1, sellers / 3d);
        var provenance = realEvidence == 0 ? 0.6 : 1;
        return Math.Round((decimal)Math.Clamp((0.2 + 0.8 * coverage) * provenance, 0, 1), 4);
    }
}
