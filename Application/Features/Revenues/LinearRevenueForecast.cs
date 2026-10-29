using System;
using System.Collections.Generic;
using System.Linq;

namespace APCS.Application.Features.Revenues;

/// <summary>
/// Ordinary least squares forecast over a weekly revenue series, with 95% prediction intervals.
/// </summary>
/// <remarks>
/// The model is <c>y = a + b·x</c> where <c>x</c> is the zero-based week index. The optimistic and
/// conservative scenarios are the upper and lower bounds of the prediction interval
/// <c>t(0.975, n-2) · se · sqrt(1 + 1/n + (x - x̄)² / Sxx)</c>, never arbitrary multipliers.
/// </remarks>
internal static class LinearRevenueForecast
{
    /// <summary>Below this many weeks no forecast is produced.</summary>
    public const int MinimumWeeks = 8;

    /// <summary>From this many weeks on the forecast is considered reliable.</summary>
    public const int ReliableWeeks = 14;

    public const string StatusOk = "ok";
    public const string StatusLowConfidence = "low_confidence";
    public const string StatusInsufficient = "insufficient";

    // Two-sided 95% critical values t(0.975, df) for df = 1..30.
    private static readonly double[] TCriticalByDf =
    [
        12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228,
        2.201, 2.179, 2.160, 2.145, 2.131, 2.120, 2.110, 2.101, 2.093, 2.086,
        2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042
    ];

    /// <summary>
    /// Fits the series and projects <paramref name="horizonWeeks"/> weeks past its last point.
    /// </summary>
    /// <param name="weeklyRevenue">Revenue per completed week, oldest first.</param>
    /// <param name="horizonWeeks">How many future weeks to project.</param>
    /// <returns>
    /// The fit; <see cref="LinearForecastResult.Steps"/> starts at offset 0 (the last observed week,
    /// so a chart can join the forecast to the actual series) and is empty when insufficient.
    /// </returns>
    public static LinearForecastResult Fit(IReadOnlyList<decimal> weeklyRevenue, int horizonWeeks)
    {
        ArgumentNullException.ThrowIfNull(weeklyRevenue);
        ArgumentOutOfRangeException.ThrowIfNegative(horizonWeeks);

        int n = weeklyRevenue.Count;
        if (n < MinimumWeeks)
        {
            return new LinearForecastResult(StatusInsufficient, n, 0, 0, 0, 0, []);
        }

        var y = weeklyRevenue.Select(v => (double)v).ToArray();
        double xMean = (n - 1) / 2.0;
        double yMean = y.Average();

        double sxx = 0, sxy = 0;
        for (int x = 0; x < n; x++)
        {
            sxx += (x - xMean) * (x - xMean);
            sxy += (x - xMean) * (y[x] - yMean);
        }

        double slope = sxy / sxx;
        double intercept = yMean - slope * xMean;

        double sse = 0, sst = 0;
        for (int x = 0; x < n; x++)
        {
            double residual = y[x] - (intercept + slope * x);
            sse += residual * residual;
            sst += (y[x] - yMean) * (y[x] - yMean);
        }

        // A constant series has no variance to explain, so R² is reported as 0 rather than NaN.
        double rSquared = sst > 0 ? 1 - sse / sst : 0;
        double standardError = Math.Sqrt(sse / (n - 2));
        double t = TCritical(n - 2);

        var steps = new List<LinearForecastStep>(horizonWeeks + 1);
        for (int offset = 0; offset <= horizonWeeks; offset++)
        {
            int x = n - 1 + offset;
            double mid = intercept + slope * x;
            double margin = t * standardError * Math.Sqrt(1 + 1.0 / n + (x - xMean) * (x - xMean) / sxx);

            steps.Add(new LinearForecastStep(
                offset,
                ToMoney(mid),
                ToMoney(mid + margin),
                ToMoney(mid - margin)));
        }

        string status = n < ReliableWeeks ? StatusLowConfidence : StatusOk;
        return new LinearForecastResult(status, n, intercept, slope, rSquared, standardError, steps);
    }

    /// <summary>Gets t(0.975, df), falling back to the next lower tabulated df (a wider interval).</summary>
    internal static double TCritical(int degreesOfFreedom)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(degreesOfFreedom, 1);

        if (degreesOfFreedom <= TCriticalByDf.Length) return TCriticalByDf[degreesOfFreedom - 1];
        if (degreesOfFreedom < 40) return 2.042;
        if (degreesOfFreedom < 60) return 2.021;
        if (degreesOfFreedom < 120) return 2.000;
        return 1.980;
    }

    // Revenue cannot be negative, so every projected value is floored at zero.
    private static decimal ToMoney(double value) => Math.Round((decimal)Math.Max(0, value), 2);
}

internal sealed record LinearForecastResult(
    string Status,
    int SampleSize,
    double Intercept,
    double Slope,
    double RSquared,
    double StandardError,
    IReadOnlyList<LinearForecastStep> Steps);

internal sealed record LinearForecastStep(
    int WeekOffset,
    decimal Base,
    decimal Optimistic,
    decimal Conservative);
