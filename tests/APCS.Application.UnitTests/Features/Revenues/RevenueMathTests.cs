using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class RevenueMathTests
{
    [TestMethod]
    public void PercentageChange_WhenPreviousIsZeroAndCurrentIsPositive_IsUndefinedRatherThan100()
    {
        RevenueMath.PercentageChange(819.92m, 0m).Should().BeNull();
    }

    [TestMethod]
    public void PercentageChange_WhenBothPeriodsAreZero_IsUndefined()
    {
        RevenueMath.PercentageChange(0m, 0m).Should().BeNull();
    }

    [TestMethod]
    [DataRow(300.0, 150.0, 100.0)]
    [DataRow(150.0, 300.0, -50.0)]
    [DataRow(150.0, 150.0, 0.0)]
    [DataRow(1.0, 3.0, -66.67)]
    public void PercentageChange_WhenPreviousIsPositive_ReturnsRoundedChange(double current, double previous, double expected)
    {
        RevenueMath.PercentageChange((decimal)current, (decimal)previous).Should().Be((decimal)expected);
    }

    [TestMethod]
    public void PercentageChange_WhenPreviousIsNegative_DividesByMagnitudeSoTheSignShowsDirection()
    {
        // Net revenue recovering from -100 to +50 went up, so the change must be positive.
        RevenueMath.PercentageChange(50m, -100m).Should().Be(150m);
        // Falling further from -100 to -150 went down.
        RevenueMath.PercentageChange(-150m, -100m).Should().Be(-50m);
    }

    [TestMethod]
    public void PercentageChange_WhenCurrentIsNegative_ReportsAFallBeyondMinus100()
    {
        RevenueMath.PercentageChange(-50m, 100m).Should().Be(-150m);
    }
}
