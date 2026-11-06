using APCS.Application.Features.Admin.SubscriptionPlans;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class TrialConversionTests
{
    [TestMethod]
    public void Rate_LeavesRunningTrialsOut()
    {
        // 50 started: 12 bought, 27 went back to Free, 11 still running. 12 / (12 + 27).
        TrialConversion.Rate(12, 27).Should().Be(30.8m);
    }

    [TestMethod]
    public void Rate_WithNoFinishedTrial_IsUnknownNotZero()
    {
        TrialConversion.Rate(0, 0).Should().BeNull();
    }

    [TestMethod]
    [DataRow(0, 5, 0)]
    [DataRow(4, 0, 100)]
    public void Rate_AtTheEnds_IsZeroOrAHundred(int converted, int ended, int expected)
    {
        TrialConversion.Rate(converted, ended).Should().Be(expected);
    }
}
