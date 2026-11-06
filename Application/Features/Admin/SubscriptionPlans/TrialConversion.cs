namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>How many finished trials bought a plan.</summary>
/// <remarks>
/// Only finished trials have an outcome: one that bought a plan while it ran, or one that expired or
/// was cancelled and went back to Free. Trials still running count in neither part, so the rate
/// does not fall just because many trials started recently; both periods are measured this way.
/// </remarks>
internal static class TrialConversion
{
    /// <summary>Converted trials as a share of converted plus ended ones, 0–100; null when none has finished.</summary>
    public static decimal? Rate(int converted, int endedWithoutBuying)
    {
        var finished = converted + endedWithoutBuying;
        return finished == 0 ? null : Math.Round(converted * 100m / finished, 1);
    }
}
