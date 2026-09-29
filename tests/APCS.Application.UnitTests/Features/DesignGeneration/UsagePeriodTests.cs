using APCS.Application.Features.DesignGeneration.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.DesignGeneration;

[TestClass]
public sealed class UsagePeriodTests
{
    [TestMethod]
    public void Containing_AnnualPlanRenewingNextYear_ReturnsCurrentMonthlyWindow()
    {
        var (start, end) = UsagePeriod.Containing(new DateOnly(2026, 9, 26), new DateOnly(2027, 9, 19));

        start.Should().Be(new DateOnly(2026, 9, 19));
        end.Should().Be(new DateOnly(2026, 10, 19));
    }

    [TestMethod]
    public void Containing_MonthlyPlanRenewingNextMonth_ReturnsPreviousMonthToRenewal()
    {
        var (start, end) = UsagePeriod.Containing(new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 19));

        start.Should().Be(new DateOnly(2026, 9, 19));
        end.Should().Be(new DateOnly(2026, 10, 19));
    }

    [TestMethod]
    public void Containing_RenewalDateInThePast_RollsForwardToTheCurrentWindow()
    {
        var (start, end) = UsagePeriod.Containing(new DateOnly(2026, 9, 26), new DateOnly(2026, 6, 19));

        start.Should().BeOnOrBefore(new DateOnly(2026, 9, 26));
        end.Should().BeOnOrAfter(new DateOnly(2026, 9, 26));
        end.Should().Be(new DateOnly(2026, 10, 19));
    }

    [TestMethod]
    public void Containing_ResultAlwaysContainsToday()
    {
        var today = new DateOnly(2026, 9, 26);
        foreach (var renewal in new[] { new DateOnly(2027, 9, 19), new DateOnly(2026, 10, 3), new DateOnly(2026, 9, 26), new DateOnly(2028, 2, 29) })
        {
            var (start, end) = UsagePeriod.Containing(today, renewal);
            start.Should().BeOnOrBefore(today);
            end.Should().BeOnOrAfter(today);
        }
    }
}
