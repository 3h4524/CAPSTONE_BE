using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Shops.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Shops;

[TestClass]
public sealed class PrintPlacementTests
{
    private static readonly MockupPosition Frame = new(100, 100, 400, 500);

    [TestMethod]
    public void FromMockup_ATurnedBox_KeepsItsPlaceAndSizeAndSendsTheAngleInWholeDegrees()
    {
        var placement = PrintPlacement.FromMockup(Frame, new MockupPosition(200, 125, 200, 250), 1000, 1000, 4500, 5100, rotation: 14.6);

        placement.Should().Be(new PrintPlacement(0.5, 0.3, 0.5, false, 15));
    }

    [TestMethod]
    public void FromMockup_TheWholeFrameTurned_IsShrunkUntilItsCornersStayInside()
    {
        var placement = PrintPlacement.FromMockup(Frame, Frame, 1000, 1000, 4500, 5100, rotation: 45);

        placement.Angle.Should().Be(45);
        placement.Adjusted.Should().BeTrue();
        // A square turned 45° is √2 times as wide: it fits the 4500-wide area at 1/√2 of its width.
        placement.Scale.Should().BeApproximately(1 / Math.Sqrt(2), 0.0001);
        placement.X.Should().Be(0.5);
        placement.Y.Should().Be(0.5);
    }

    [TestMethod]
    public void FromMockup_AnAngleOutsideHalfATurn_IsReadTheShortWayRound()
    {
        PrintPlacement.FromMockup(Frame, new MockupPosition(200, 125, 200, 250), 1000, 1000, 4500, 5100, rotation: 350).Angle.Should().Be(-10);
        PrintPlacement.FromMockup(Frame, new MockupPosition(200, 125, 200, 250), 1000, 1000, 4500, 5100, rotation: -190).Angle.Should().Be(170);
    }
}
