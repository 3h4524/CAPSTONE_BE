namespace APCS.Application.Features.SellerDashboard.Common;

/// <summary>
/// Ranks action items: <c>severity × 10 + items held back + hours waited</c>, with the hours capped at 48.
/// A failed upload holding 8 products back for 5 hours scores 3 × 10 + 8 + 5 = 43 and comes before
/// 12 designs waiting 2 hours for review (2 × 10 + 12 + 2 = 34). The score alone would let a review left
/// for two days (2 × 10 + 1 + 48 = 69) pass a fresh critical item (4 × 10 + 1 + 1 = 42), so items are
/// ordered by <see cref="Weight"/> first and by the score within one severity.
/// </summary>
public static class ActionItemPriority
{
    public const int MaxHoursWaited = 48;

    public static int Calculate(string severity, int count, DateTime? oldestAt, DateTime now)
    {
        var hours = oldestAt is DateTime since ? (int)Math.Clamp((now - since).TotalHours, 0, MaxHoursWaited) : 0;
        return Weight(severity) * 10 + count + hours;
    }

    public static int Weight(string severity) => severity switch
    {
        ActionItemSeverities.Critical => 4,
        ActionItemSeverities.Error => 3,
        ActionItemSeverities.Warning => 2,
        _ => 1,
    };
}
