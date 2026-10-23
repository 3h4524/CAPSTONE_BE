using System;
using System.Collections.Generic;
using System.Linq;
using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class LinearRevenueForecastTests
{
    [TestMethod]
    public void Fit_SteadilyIncreasingSeries_FitsPerfectlyAndProjectsUpward()
    {
        var weekly = Enumerable.Range(0, 16).Select(x => 1000m + 500m * x).ToList();

        var result = LinearRevenueForecast.Fit(weekly, horizonWeeks: 4);

        result.Status.Should().Be(LinearRevenueForecast.StatusOk);
        result.SampleSize.Should().Be(16);
        result.Slope.Should().BeApproximately(500, 1e-9);
        result.Intercept.Should().BeApproximately(1000, 1e-9);
        result.RSquared.Should().BeApproximately(1, 1e-12);
        result.StandardError.Should().BeApproximately(0, 1e-9);

        result.Steps.Should().HaveCount(5, "offset 0 anchors on the last observed week, then 4 projected weeks");
        result.Steps[0].Base.Should().Be(8500m, "the fitted value at the last observed week (x = 15)");
        result.Steps[4].Base.Should().Be(10500m, "x = 19 on y = 1000 + 500x");
        result.Steps.Select(s => s.Base).Should().BeInAscendingOrder();
        result.Steps.Should().OnlyContain(s => s.Optimistic == s.Base && s.Conservative == s.Base,
            "a perfect fit leaves no residual error, so the prediction interval collapses to the line");
    }

    [TestMethod]
    public void Fit_KnownSeries_MatchesHandComputedRegressionAndInterval()
    {
        // x̄ = 3.5, ȳ = 5.5, Sxx = 42, Sxy = 38, SST = 38, SSE = 38 - 38²/42.
        var weekly = new List<decimal> { 2, 4, 5, 4, 5, 7, 8, 9 };

        var result = LinearRevenueForecast.Fit(weekly, horizonWeeks: 1);

        double slope = 38.0 / 42.0;
        double intercept = 5.5 - slope * 3.5;
        double sse = 38.0 - 38.0 * 38.0 / 42.0;
        double se = Math.Sqrt(sse / 6);
        double margin = 2.447 * se * Math.Sqrt(1 + 1.0 / 8 + (8 - 3.5) * (8 - 3.5) / 42.0);
        double mid = intercept + slope * 8;

        result.Status.Should().Be(LinearRevenueForecast.StatusLowConfidence);
        result.Slope.Should().BeApproximately(slope, 1e-9);
        result.Intercept.Should().BeApproximately(intercept, 1e-9);
        result.RSquared.Should().BeApproximately(1444.0 / (42.0 * 38.0), 1e-9);
        result.StandardError.Should().BeApproximately(se, 1e-9);

        var next = result.Steps[1];
        ((double)next.Base).Should().BeApproximately(mid, 0.005);
        ((double)next.Optimistic).Should().BeApproximately(mid + margin, 0.005);
        ((double)next.Conservative).Should().BeApproximately(mid - margin, 0.005);
    }

    [TestMethod]
    public void Fit_AllZeroSeries_ProjectsZeroWithoutNaN()
    {
        var weekly = Enumerable.Repeat(0m, 10).ToList();

        var result = LinearRevenueForecast.Fit(weekly, horizonWeeks: 13);

        result.Status.Should().Be(LinearRevenueForecast.StatusLowConfidence);
        result.Slope.Should().Be(0);
        result.RSquared.Should().Be(0, "a constant series has no variance to explain");
        double.IsNaN(result.StandardError).Should().BeFalse();
        result.Steps.Should().HaveCount(14);
        result.Steps.Should().OnlyContain(s => s.Base == 0m && s.Optimistic == 0m && s.Conservative == 0m);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(7)]
    public void Fit_FewerThanEightWeeks_IsInsufficientAndProjectsNothing(int weeks)
    {
        var weekly = Enumerable.Range(0, weeks).Select(x => 100m * (x + 1)).ToList();

        var result = LinearRevenueForecast.Fit(weekly, horizonWeeks: 13);

        result.Status.Should().Be(LinearRevenueForecast.StatusInsufficient);
        result.SampleSize.Should().Be(weeks);
        result.Steps.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(8, LinearRevenueForecast.StatusLowConfidence)]
    [DataRow(13, LinearRevenueForecast.StatusLowConfidence)]
    [DataRow(14, LinearRevenueForecast.StatusOk)]
    public void Fit_SampleSizeAtThresholds_ReportsMatchingConfidence(int weeks, string expectedStatus)
    {
        var weekly = Enumerable.Range(0, weeks).Select(x => 100m + 10m * x).ToList();

        LinearRevenueForecast.Fit(weekly, horizonWeeks: 1).Status.Should().Be(expectedStatus);
    }

    [TestMethod]
    public void Fit_SeriesWithOutlier_WidensTheIntervalAndNeverGoesNegative()
    {
        var clean = new List<decimal> { 100, 120, 110, 130, 125, 140, 135, 150, 145, 160, 155, 170, 165, 180 };
        var withOutlier = clean.ToList();
        withOutlier[6] = 2000m;

        var cleanFit = LinearRevenueForecast.Fit(clean, horizonWeeks: 13);
        var outlierFit = LinearRevenueForecast.Fit(withOutlier, horizonWeeks: 13);

        cleanFit.Slope.Should().BeGreaterThan(0);
        cleanFit.RSquared.Should().BeGreaterThan(0.9);
        outlierFit.RSquared.Should().BeLessThan(cleanFit.RSquared);

        decimal Width(LinearForecastResult r) => r.Steps[^1].Optimistic - r.Steps[^1].Conservative;
        Width(outlierFit).Should().BeGreaterThan(Width(cleanFit) * 5,
            "the outlier inflates the residual error and so the prediction interval");
        outlierFit.Steps.Should().OnlyContain(s => s.Conservative >= 0m && s.Base >= 0m,
            "revenue projections are floored at zero");
    }

    [TestMethod]
    [DataRow(1, 12.706)]
    [DataRow(6, 2.447)]
    [DataRow(30, 2.042)]
    [DataRow(35, 2.042)]
    [DataRow(100, 2.000)]
    [DataRow(500, 1.980)]
    public void TCritical_ReturnsTabulatedOrNextLowerValue(int df, double expected)
    {
        LinearRevenueForecast.TCritical(df).Should().Be(expected);
    }
}
