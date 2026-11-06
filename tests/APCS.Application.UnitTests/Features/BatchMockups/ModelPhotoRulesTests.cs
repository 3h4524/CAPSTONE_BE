using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class ModelPhotoRulesTests
{
    private static readonly MockupPosition TemplateArea = new(800, 600, 900, 1200);
    private static readonly MockupPosition ModelArea = new(400, 500, 300, 400);

    [TestMethod]
    public void MapPlacement_TemplatesOwnPrintArea_IsTheModelPhotosPrintArea()
    {
        MockupRules.MapPlacement(TemplateArea, TemplateArea, ModelArea, 1000, 1500).Should().Be(ModelArea);
    }

    [TestMethod]
    public void MapPlacement_PrintAreaMovedOnTheMockup_MovesTheSameShareOfTheModelPhotosOne()
    {
        // The top half of the template's print area, pushed right by a third of its width.
        var used = new MockupPosition(1100, 600, 900, 600);

        var mapped = MockupRules.MapPlacement(used, TemplateArea, ModelArea, 1000, 1500);

        mapped.Should().Be(new MockupPosition(500, 500, 300, 200));
    }

    [TestMethod]
    public void MapPlacement_ReachingPastTheModelPhoto_StaysInsideIt()
    {
        var used = new MockupPosition(1500, 1500, 900, 1200);

        var mapped = MockupRules.MapPlacement(used, TemplateArea, ModelArea, 800, 1000);

        (mapped.X + mapped.Width).Should().BeLessThanOrEqualTo(800);
        (mapped.Y + mapped.Height).Should().BeLessThanOrEqualTo(1000);
        mapped.Width.Should().BePositive();
        mapped.Height.Should().BePositive();
    }

    [TestMethod]
    public void MapPlacementByGarment_KeepsTheSizeAndPlaceRelativeToTheGarmentsBody()
    {
        // Template 2000 x 2000: body 1000 px wide, centered at 1000, garment from y 200.
        // Model 1000 x 1500: body 300 px wide, centered at 450, garment from y 300.
        var template = new GarmentMeasurement(0.1, 0.9, 0.5, 0.5);
        var model = new GarmentMeasurement(0.2, 0.8, 0.45, 0.3);
        var used = new MockupPosition(700, 400, 600, 800);

        var mapped = MockupRules.MapPlacementByGarment(used, template, 2000, 2000, model, 1000, 1500);

        // The bodies scale by 0.3, and the model's shows three quarters of its flat width: 0.4 in all.
        // 240 x 320, 120 px left of the body's center, 80 px below the garment's top.
        mapped.Should().Be(new MockupPosition(330, 380, 240, 320));
    }

    [TestMethod]
    public void MapPlacementByGarment_KeepsTheDesignsShape()
    {
        var mapped = MockupRules.MapPlacementByGarment(new MockupPosition(800, 600, 900, 1200),
            new GarmentMeasurement(0.1, 0.9, 0.5, 0.6), 2400, 2400, new GarmentMeasurement(0.2, 0.7, 0.5, 0.35), 1000, 1500);

        ((double)mapped.Width / mapped.Height).Should().BeApproximately(900.0 / 1200, 0.01);
    }

    [TestMethod]
    public void SerializePrintArea_Automatic_IsReadBackAsAutomaticAndKeepsTheArea()
    {
        var automatic = MockupRules.SerializePrintArea(ModelArea, automatic: true);
        var byHand = MockupRules.SerializePrintArea(ModelArea, automatic: false);

        MockupRules.IsAutomaticPlacement(automatic).Should().BeTrue();
        MockupRules.ParsePrintArea(automatic).Should().Be(ModelArea);
        MockupRules.IsAutomaticPlacement(byHand).Should().BeFalse();
        MockupRules.IsAutomaticPlacement("{\"auto\":\"yes\"}").Should().BeFalse();
        MockupRules.IsAutomaticPlacement("not json").Should().BeFalse();
        MockupRules.IsAutomaticPlacement(null).Should().BeFalse();
    }

    // ---- A print area marked by its four corners ----

    private static readonly MockupPoint[] Trapezoid = [new(400, 500), new(700, 520), new(690, 900), new(410, 880)];

    [TestMethod]
    public void SquareToQuad_TakesTheSquaresCornersToTheQuadrilaterals()
    {
        var toQuad = MockupRules.SquareToQuad(Trapezoid);

        foreach (var (u, v, corner) in new[] { (0.0, 0.0, Trapezoid[0]), (1.0, 0.0, Trapezoid[1]), (1.0, 1.0, Trapezoid[2]), (0.0, 1.0, Trapezoid[3]) })
        {
            var point = toQuad(u, v);
            point.X.Should().BeApproximately(corner.X, 1e-6);
            point.Y.Should().BeApproximately(corner.Y, 1e-6);
        }
    }

    [TestMethod]
    public void MapPlacementOnQuad_StraightPrintArea_PlacesTheDesignAsTheRectangleDoes()
    {
        // A square design in the 900 x 1200 print area fills its width and an eighth is left above and below it.
        MockupPoint[] straight = [new(400, 500), new(700, 500), new(700, 900), new(400, 900)];

        var corners = MockupRules.MapPlacementOnQuad(TemplateArea, TemplateArea, 1024, 1024, straight);

        corners.Should().BeEquivalentTo(new[] { new MockupPoint(400, 550), new MockupPoint(700, 550), new MockupPoint(700, 850), new MockupPoint(400, 850) },
            options => options.WithStrictOrdering().Using<double>(ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 1e-6)).WhenTypeIs<double>());
    }

    [TestMethod]
    public void MapPlacementOnQuad_DesignFillingThePrintArea_TakesItsCorners()
    {
        var corners = MockupRules.MapPlacementOnQuad(TemplateArea, TemplateArea, 900, 1200, Trapezoid);

        for (var i = 0; i < 4; i++)
        {
            corners[i].X.Should().BeApproximately(Trapezoid[i].X, 1e-6);
            corners[i].Y.Should().BeApproximately(Trapezoid[i].Y, 1e-6);
        }
    }

    [TestMethod]
    public void IsValidQuad_NeedsTheCornersClockwiseWithoutCrossingAndInsideThePhoto()
    {
        MockupRules.IsValidQuad(Trapezoid, 1000, 1500).Should().BeTrue();
        MockupRules.IsValidQuad([Trapezoid[0], Trapezoid[2], Trapezoid[1], Trapezoid[3]], 1000, 1500).Should().BeFalse("the edges cross");
        MockupRules.IsValidQuad([Trapezoid[0], Trapezoid[3], Trapezoid[2], Trapezoid[1]], 1000, 1500).Should().BeFalse("it goes round the other way");
        MockupRules.IsValidQuad(Trapezoid, 650, 1500).Should().BeFalse("a corner is off the photo");
        MockupRules.IsValidQuad(Trapezoid.Take(3).ToArray(), 1000, 1500).Should().BeFalse();
    }

    [TestMethod]
    public void SerializePrintQuad_IsReadBackWithTheBoxAroundIt()
    {
        var json = MockupRules.SerializePrintQuad(Trapezoid);

        MockupRules.ParsePrintQuad(json).Should().BeEquivalentTo(Trapezoid, options => options.WithStrictOrdering());
        MockupRules.ParsePrintArea(json).Should().Be(new MockupPosition(400, 500, 300, 400));
        MockupRules.IsAutomaticPlacement(json).Should().BeFalse();
        MockupRules.ParsePrintQuad(MockupRules.SerializePrintArea(ModelArea)).Should().BeNull();
        MockupRules.ParsePrintQuad("{\"quad\":[[1,2],[3,4]]}").Should().BeNull();
    }

    [TestMethod]
    [DataRow(0.15, 0.85, null)]
    [DataRow(0.03, 0.85, null)]
    [DataRow(0.01, 0.85, "select more of the garment")]
    [DataRow(0.95, 0.85, "select only the garment")]
    [DataRow(0.15, 0.5, "the garment is too dark")]
    public void ModelPhotoProblem_AllowsASmallLightGarmentOnly(double coverage, double luminance, string? problem)
    {
        var result = MockupRules.ModelPhotoProblem(new GarmentMaskStats(coverage, luminance));

        if (problem is null)
            result.Should().BeNull();
        else
            result.Should().StartWith(problem);
    }
}
