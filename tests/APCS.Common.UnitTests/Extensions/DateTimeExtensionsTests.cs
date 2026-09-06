using APCS.Common.Extensions;
using FluentAssertions;

namespace APCS.Common.UnitTests.Extensions;

[TestClass]
public sealed class DateTimeExtensionsTests
{
    [TestMethod]
    public void ToUnixTimestamp_ForKnownValue_ReturnsSeconds()
    {
        var value = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        value.ToUnixTimestamp().Should().Be(1_700_000_000);
    }

    [TestMethod]
    [DataRow(-1, true)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    public void IsExpired_AtBoundary_ReturnsExpected(int secondsFromNow, bool expected)
    {
        var now = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        now.AddSeconds(secondsFromNow).IsExpired(now).Should().Be(expected);
    }
}
