using System;

namespace APCS.Application.Features.Revenues;

/// <summary>Period-over-period arithmetic shared by the revenue endpoints.</summary>
internal static class RevenueMath
{
    /// <summary>
    /// Gets the percentage change from <paramref name="previous"/> to <paramref name="current"/>,
    /// rounded to two decimals.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the previous period is zero, because any change from nothing is
    /// undefined rather than "+100%"; the client shows that as "—" or "New". A negative previous value
    /// (e.g. net revenue after heavy refunds) is divided by its magnitude so the sign still says
    /// whether the value went up or down.
    /// </returns>
    public static decimal? PercentageChange(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return null;
        }

        return Math.Round((current - previous) / Math.Abs(previous) * 100m, 2);
    }
}
