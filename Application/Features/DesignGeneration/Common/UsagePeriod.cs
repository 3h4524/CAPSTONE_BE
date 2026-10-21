namespace APCS.Application.Features.DesignGeneration.Common;

/// <summary>Computes the monthly usage window that contains a given day.</summary>
public static class UsagePeriod
{
    /// <summary>
    /// Returns the one-month window (start inclusive, end inclusive) that contains
    /// <paramref name="today"/>, aligned to the subscription's renewal day. Annual plans renew a year
    /// ahead but their image quota is per month, so the window is walked back month by month from the
    /// renewal date instead of being taken as "one month before renewal".
    /// </summary>
    public static (DateOnly Start, DateOnly End) Containing(DateOnly today, DateOnly renewalDate)
    {
        var end = renewalDate;
        // Bounded so a bad renewal date can never spin forever.
        for (var i = 0; i < 600 && end.AddMonths(-1) > today; i++)
            end = end.AddMonths(-1);
        for (var i = 0; i < 600 && end < today; i++)
            end = end.AddMonths(1);

        return (end.AddMonths(-1), end);
    }
}
