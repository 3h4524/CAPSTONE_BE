using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class MockupSupersessionTests
{
    private static readonly Guid Design = Guid.NewGuid();
    private static readonly Guid Front = Guid.NewGuid();
    private static readonly Guid Back = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 11, 9, 14, 0, 0, DateTimeKind.Utc);

    private static MockupImage Composite(Guid template, string color, int minute, bool stored = true, Guid? design = null) => new()
    {
        Id = Guid.NewGuid(),
        DesignImageId = design ?? Design,
        ProductId = Guid.NewGuid(),
        MockupTemplateId = template,
        SourceType = MockupSources.Generated,
        ContentHash = stored ? "hash" : null,
        VariantKey = color,
        GarmentColor = color,
        ApprovalStatus = "approved",
        CreatedAt = Start.AddMinutes(minute),
    };

    [TestMethod]
    public void Superseded_ANewerStoredCompositeOfTheSameTemplateAndColor_ReplacesTheOlderOne()
    {
        var older = Composite(Front, "#FFFFFF", 0);
        var newer = Composite(Front, "#FFFFFF", 5);

        MockupSources.Superseded([older, newer]).Should().BeEquivalentTo([older.Id]);
        MockupSources.Current([older, newer]).Should().ContainSingle().Which.Should().BeSameAs(newer);
    }

    [TestMethod]
    public void Superseded_ANewerCompositeNotStoredYet_KeepsTheOlderOneUntilItIsImported()
    {
        var older = Composite(Front, "#FFFFFF", 0);
        var waiting = Composite(Front, "#FFFFFF", 5, stored: false);

        MockupSources.Superseded([older, waiting]).Should().BeEmpty();
    }

    [TestMethod]
    public void Superseded_OtherColorsTemplatesAndDesigns_AreEachTheirOwnMockUp()
    {
        var rows = new[]
        {
            Composite(Front, "#FFFFFF", 0),
            Composite(Front, "#1A1A1A", 5),
            Composite(Back, "#FFFFFF", 6),
            Composite(Front, "#FFFFFF", 7, design: Guid.NewGuid()),
        };

        MockupSources.Superseded(rows).Should().BeEmpty();
    }

    [TestMethod]
    public void Superseded_TheColorIsComparedWithoutCase()
    {
        var older = Composite(Front, "#1a1a1a", 0);
        var newer = Composite(Front, "#1A1A1A", 5);

        MockupSources.Superseded([older, newer]).Should().BeEquivalentTo([older.Id]);
    }

    [TestMethod]
    public void Superseded_UploadsLifestyleShotsAndKeyframes_AreNeverReplaced()
    {
        var upload = new MockupImage { Id = Guid.NewGuid(), SourceType = MockupSources.Uploaded, ContentHash = "a", CreatedAt = Start };
        var later = new MockupImage { Id = Guid.NewGuid(), SourceType = MockupSources.Uploaded, ContentHash = "b", CreatedAt = Start.AddMinutes(1) };
        var frame = new MockupImage { Id = Guid.NewGuid(), SourceType = MockupSources.AiGenerated, ContentHash = "c", CreatedAt = Start, DesignImageId = Design };

        MockupSources.Superseded([upload, later, frame]).Should().BeEmpty();
    }
}
