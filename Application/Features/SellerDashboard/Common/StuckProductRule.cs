namespace APCS.Application.Features.SellerDashboard.Common;

/// <summary>
/// Decides when a product has been processing too long: three times the Seller's average step duration,
/// or 15 minutes while there is no history yet. With an average of 90 seconds the threshold is 270 seconds,
/// so a product that started 6 minutes (360 seconds) ago counts as stuck.
/// </summary>
public static class StuckProductRule
{
    public const decimal Multiplier = 3;
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromMinutes(15);

    public static TimeSpan Threshold(decimal? averageDurationSeconds) =>
        averageDurationSeconds is > 0 and var average
            ? TimeSpan.FromSeconds((double)(average * Multiplier))
            : DefaultThreshold;

    public static bool IsStuck(DateTime startedAt, DateTime now, TimeSpan threshold) => now - startedAt > threshold;
}
