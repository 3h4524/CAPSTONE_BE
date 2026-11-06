using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class PlanMetricsTests
{
    [TestMethod]
    public void Arpu_WithPayingCustomers_DividesRevenueByCustomers()
    {
        PlanMetrics.Arpu(300m, 4).Should().Be(75m);
        PlanMetrics.Arpu(100m, 3).Should().Be(33.33m);
    }

    [TestMethod]
    public void Arpu_WithZeroCustomers_IsUndefined()
    {
        PlanMetrics.Arpu(0m, 0).Should().BeNull();
        PlanMetrics.Arpu(150m, 0).Should().BeNull();
    }
    [TestMethod]
    public void RetentionRate_CountsCustomersWhoSwitchedPlanAsRetained()
    {
        // 6 at the start: 4 stayed, 1 moved to another plan, 1 stopped paying.
        var flow = new MrrCustomerFlow(Start: 6, Stayed: 4, SwitchedOut: 1, StoppedPaying: 1, New: 3, SwitchedIn: 0, End: 7);

        PlanMetrics.RetentionRate(flow).Should().Be(83.3m, "5 of the 6 still pay");
    }

    [TestMethod]
    public void RetentionRate_BelowTheMinimumSample_IsUndefined()
    {
        var flow = new MrrCustomerFlow(Start: 4, Stayed: 4, SwitchedOut: 0, StoppedPaying: 0, New: 0, SwitchedIn: 0, End: 4);

        PlanMetrics.RetentionRate(flow).Should().BeNull("4 customers is below the minimum sample of 5");
    }
}
