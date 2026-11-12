using System.Text.Json.Nodes;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class MockupPrintAreaRulesTests
{
    private static readonly MockupAreaDto Main = new(MockupRules.MainAreaKey, "Chest", 800, 600, 900, 1100, Source: MockupRules.SourceSame);
    private static readonly MockupAreaDto Sleeve = new("sleeve-left", "Left sleeve", 300, 500, 200, 200, -15, MockupRules.SourceNone);

    [TestMethod]
    public void ParsePrintLayout_OneRectangleAsEveryTemplateHadBefore_IsOneMainAreaOnTheFront()
    {
        var layout = MockupRules.ParsePrintLayout("{\"x\":820,\"y\":640,\"width\":900,\"height\":1100,\"unit\":\"px\"}");

        layout!.View.Should().Be(MockupRules.ViewFront);
        layout.Areas.Should().ContainSingle().Which.Should()
            .Be(new MockupAreaDto(MockupRules.MainAreaKey, null, 820, 640, 900, 1100, 0, MockupRules.SourceSame));
    }

    [TestMethod]
    public void SerializePrintLayout_OneStraightAreaOnTheFront_IsStoredExactlyAsBefore()
    {
        var area = new MockupAreaDto(MockupRules.MainAreaKey, null, 820, 640, 900, 1100, Source: MockupRules.SourceSame);

        MockupRules.SerializePrintLayout(null, [area]).Should().Be(MockupRules.SerializePrintArea(MockupRules.AreaPosition(area)));
    }

    [TestMethod]
    public void SerializePrintLayout_SeveralAreasOnTheBack_ReadBackTheSame()
    {
        var json = MockupRules.SerializePrintLayout(MockupRules.ViewBack, [Main, Sleeve]);

        var layout = MockupRules.ParsePrintLayout(json)!;
        layout.View.Should().Be(MockupRules.ViewBack);
        layout.Areas.Should().Equal(Main, Sleeve);
        // Code that knows only one print area still reads the main one.
        MockupRules.ParsePrintArea(json).Should().Be(MockupRules.AreaPosition(Main));
    }

    [TestMethod]
    public void ParsePrintLayout_AreasThatCannotBeRead_AreLeftOut()
    {
        var layout = MockupRules.ParsePrintLayout(
            "{\"x\":0,\"y\":0,\"width\":100,\"height\":100,\"areas\":["
            + "{\"key\":\"main\",\"x\":1,\"y\":1,\"width\":5,\"height\":5},"
            + "{\"key\":\"Bad Key\",\"x\":1,\"y\":1,\"width\":5,\"height\":5},"
            + "{\"key\":\"flat\",\"x\":1,\"y\":1,\"width\":0,\"height\":5},"
            + "{\"key\":\"ok\",\"x\":1,\"y\":1,\"width\":5,\"height\":5,\"source\":\"design\"}]}")!;

        layout.Areas.Select(area => area.Key).Should().Equal(MockupRules.MainAreaKey, "ok");
        // A template's area prints the mock-up's design or nothing; anything else is nothing.
        layout.Areas[1].Source.Should().Be(MockupRules.SourceNone);
    }

    [TestMethod]
    [DataRow(190d, -170d)]
    [DataRow(-190d, 170d)]
    [DataRow(360d, 0d)]
    [DataRow(12.345d, 12.3d)]
    public void NormalizeRotation_KeepsTheTurnWithinHalfACircleEitherWay(double degrees, double expected)
    {
        MockupRules.NormalizeRotation(degrees).Should().Be(expected);
    }

    [TestMethod]
    public void AreaFits_TurnedAreaReachingOffThePhoto_DoesNotFit()
    {
        var corner = new MockupAreaDto("a", null, 0, 0, 100, 100);

        MockupRules.AreaFits(corner, 1000, 1000).Should().BeTrue();
        MockupRules.AreaFits(corner with { Rotation = 45 }, 1000, 1000).Should().BeFalse();
        MockupRules.AreaFits(corner with { X = 100, Y = 100, Rotation = 45 }, 1000, 1000).Should().BeTrue();
    }

    [TestMethod]
    public void AreasProblem_TemplateAreas_NeedTheMainOneAndPrintOnlyTheMockupsDesignOrNothing()
    {
        MockupRules.AreasProblem([Main, Sleeve], forTemplate: true).Should().BeNull();
        MockupRules.AreasProblem([Sleeve], forTemplate: true).Should().NotBeNull();
        MockupRules.AreasProblem([Main, Main], forTemplate: true).Should().NotBeNull();
        MockupRules.AreasProblem([Main, Sleeve with { Source = MockupRules.SourceDesign, DesignImageId = Guid.NewGuid() }], forTemplate: true).Should().NotBeNull();
        MockupRules.AreasProblem([Main, Sleeve, Sleeve with { Key = "b" }, Sleeve with { Key = "c" }, Sleeve with { Key = "d" }], forTemplate: true)
            .Should().NotBeNull();
        MockupRules.AreasProblem([Main with { Rotation = 200 }], forTemplate: true).Should().NotBeNull();
    }

    [TestMethod]
    public void AreasProblem_MockupsOwnAreas_MayPrintAnotherDesignOnceItIsChosen()
    {
        MockupRules.AreasProblem([Sleeve with { Source = MockupRules.SourceDesign, DesignImageId = Guid.NewGuid() }], forTemplate: false).Should().BeNull();
        MockupRules.AreasProblem([Sleeve with { Source = MockupRules.SourceDesign }], forTemplate: false).Should().NotBeNull();
        MockupRules.AreasProblem([Sleeve with { Source = "logo" }], forTemplate: false).Should().NotBeNull();
    }

    [TestMethod]
    public void ResolveAreas_TheMockupsOwnSettingsComeFirstThenTheJobsThenTheTemplates()
    {
        var layout = new MockupRules.MockupPrintLayout(MockupRules.ViewFront, [Main, Sleeve, Sleeve with { Key = "neck" }]);
        var logo = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var custom = new MockupOverrideDto(Guid.NewGuid(), null, null, null,
            Areas: [Sleeve with { X = 10, Rotation = 5, Source = MockupRules.SourceDesign, DesignImageId = mine }]);
        var job = new Dictionary<string, MockupAreaSourceDto>
        {
            ["sleeve-left"] = new(MockupRules.SourceDesign, logo),
            ["neck"] = new(MockupRules.SourceSame),
        };

        var resolved = MockupRules.ResolveAreas(layout, custom, job);

        resolved[0].Should().Be(new MockupRules.ResolvedArea(Main, MockupRules.SourceSame, null));
        resolved[1].Source.Should().Be(MockupRules.SourceDesign);
        resolved[1].DesignImageId.Should().Be(mine);
        resolved[1].Area.X.Should().Be(10);
        resolved[1].Area.Rotation.Should().Be(5);
        resolved[2].Source.Should().Be(MockupRules.SourceSame);
    }

    [TestMethod]
    public void ResolveAreas_OlderSinglePrintAreaOfAMockup_MovesOnlyTheMainArea()
    {
        var layout = new MockupRules.MockupPrintLayout(MockupRules.ViewFront, [Main, Sleeve]);

        var resolved = MockupRules.ResolveAreas(layout, new MockupOverrideDto(Guid.NewGuid(), null, null, new MockupPrintAreaDto(1, 2, 3, 4)), null);

        resolved[0].Position.Should().Be(new APCS.Application.Abstractions.Storage.MockupPosition(1, 2, 3, 4));
        resolved[1].Area.Should().Be(Sleeve);
        resolved[1].Source.Should().Be(MockupRules.SourceNone);
    }

    [TestMethod]
    public void AreaSources_WrittenToTheJobsConfig_ReadBackWithoutTheOnesThatCannotBeKept()
    {
        var templateId = Guid.NewGuid();
        var logo = Guid.NewGuid();
        var root = new JsonObject { ["other"] = 1 };
        MockupRules.WriteAreaSources(root, new Dictionary<Guid, IReadOnlyDictionary<string, MockupAreaSourceDto>>
        {
            [templateId] = new Dictionary<string, MockupAreaSourceDto>
            {
                ["main"] = new(MockupRules.SourceSame),
                ["sleeve-left"] = new(MockupRules.SourceDesign, logo),
                ["neck"] = new(MockupRules.SourceDesign),
            },
        });

        var read = MockupRules.ReadAreaSources(root.ToJsonString());

        read[templateId].Should().HaveCount(2);
        read[templateId]["sleeve-left"].Should().Be(new MockupAreaSourceDto(MockupRules.SourceDesign, logo));
        read[templateId]["main"].Kind.Should().Be(MockupRules.SourceSame);
    }

    [TestMethod]
    public void RoleForView_TheBackOrSideOfTheGarment_IsAnotherAngle()
    {
        MockupSources.RoleForView(MockupRules.ViewFront).Should().Be(MockupSources.DefaultRole);
        MockupSources.RoleForView(MockupRules.ViewBack).Should().Be("AlternativeAngle");
        MockupSources.RoleForView(MockupRules.ViewSide).Should().Be("AlternativeAngle");
    }
}
