using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class PrintifyMockupMatcherTests
{
    private readonly Guid productId = Guid.NewGuid();
    private readonly List<MockupImage> rows = [];
    private readonly Mock<IRepository<MockupImage>> mockups = new();
    private readonly Mock<IMockupTemplatePrintifyLinkRepository> links = new();
    private readonly Mock<IPrintifyColorMapRepository> colorMap = new();
    private readonly Mock<IPrintifyService> printify = new();
    private readonly MockupTemplate tee = Template("Bella+Canvas 3001");
    private static readonly PrintifyPublishConfig Mockups = new(null, null, null, null, null, null, 10m, null, null, null, Source: "mockup");

    public PrintifyMockupMatcherTests()
    {
        mockups.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        links.Setup(x => x.GetAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TemplatePrintifyLink>());
        printify.Setup(x => x.SearchBlueprintsAsync("Bella+Canvas 3001", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<PrintifyBlueprintDto>>(
            [new(5, "Unisex Heavy Tee", "Bella+Canvas", "3001CVC", null), new(12, "Unisex Jersey Short Sleeve Tee", "Bella+Canvas", "3001", null)]));
        colorMap.Setup(x => x.NamesAsync(12, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["#000000"] = "Black", ["#1F2A44"] = "Navy" });
        printify.Setup(x => x.GetProvidersAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success<IReadOnlyList<PrintifyProviderDto>>(
            [new(1, "Cheap US", "US", null), new(2, "Full US", "US", null), new(3, "Full EU", "DE", null)]));
        Catalog(1, ["Black"], 3.99m);
        Catalog(2, ["Black", "Navy"], 4.99m);
        Catalog(3, ["Black", "Navy"], 2.99m);
    }

    private static MockupTemplate Template(string name, Guid? owner = null) =>
        new() { Id = Guid.NewGuid(), Name = name, UserId = owner, GarmentColor = "#FFFFFF" };

    private void Catalog(long provider, string[] colors, decimal shipping) =>
        printify.Setup(x => x.GetCatalogAsync(12, provider, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new PrintifyCatalogDto(12, provider,
            [], colors, ["XS", "S", "M", "L", "XL", "2XL", "5XL"], ["front"], shipping, 2m)));

    private void Mockup(MockupTemplate template, string? color, int minute = 0) => rows.Add(new MockupImage
    {
        Id = Guid.NewGuid(), ProductId = productId, MockupTemplate = template, MockupTemplateId = template.Id, GarmentColor = color,
        ApprovalStatus = "approved", MetadataRevision = 1, ApprovedRevision = 1, CreatedAt = new DateTime(2026, 11, 10, 12, minute, 0),
    });

    private PrintifyMockupMatcher Create() => new(mockups.Object, links.Object, colorMap.Object, printify.Object);

    [TestMethod]
    public async Task MatchAsync_SystemTemplate_FindsTheBlueprintByName_AndTheProviderMakingEveryColor_InTheUs()
    {
        Mockup(tee, "#000000");
        Mockup(tee, "#1f2a44", 1);

        var result = await Create().MatchAsync(productId, Mockups, default);

        var setup = result.Value!.Setup;
        setup.BlueprintId.Should().Be(12);
        setup.PrintProviderId.Should().Be(2, "it makes both colors and ships from the US");
        setup.Colors.Should().Equal("Black", "Navy");
        setup.Sizes.Should().Equal("S", "M", "L", "XL", "2XL");
        result.Value.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public async Task MatchAsync_AColorWithoutAPrintifyName_IsLeftOutAndNamed()
    {
        Mockup(tee, "#000000");
        Mockup(tee, "#ABCDEF", 1);

        var result = await Create().MatchAsync(productId, Mockups, default);

        result.Value!.Setup.Colors.Should().Equal("Black");
        result.Value.Setup.PrintProviderId.Should().Be(1, "every provider makes Black, and this US one ships cheapest");
        result.Value.Warnings.Should().ContainSingle(warning => warning.Contains("#ABCDEF"));
    }

    [TestMethod]
    public async Task MatchAsync_NoColorMapped_Fails()
    {
        Mockup(tee, "#ABCDEF");

        var result = await Create().MatchAsync(productId, Mockups, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("#ABCDEF");
    }

    [TestMethod]
    public async Task MatchAsync_TheSellersOwnTemplates_AreNotMatched()
    {
        Mockup(Template("Bella+Canvas 3001", Guid.NewGuid()), "#000000");

        var result = await Create().MatchAsync(productId, Mockups, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        printify.Verify(x => x.SearchBlueprintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task MatchAsync_ATemplateNamedAfterNoPrintifyProduct_Fails()
    {
        Mockup(Template("My cozy tee"), "#000000");
        printify.Setup(x => x.SearchBlueprintsAsync("My cozy tee", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PrintifyBlueprintDto>>([]));

        var result = await Create().MatchAsync(productId, Mockups, default);

        result.Error.Message.Should().Contain("My cozy tee");
    }

    [TestMethod]
    public async Task MatchAsync_TheSellersProvider_IsKeptWhileItMakesTheColors_AndTheLinkWinsOverTheName()
    {
        links.Setup(x => x.GetAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TemplatePrintifyLink> { [tee.Id] = new(tee.Id, 12, "Bella Canvas 3001", null, null, null, ["M", "L"]) });
        Mockup(tee, "#000000");
        Mockup(tee, "#1F2A44", 1);

        var result = await Create().MatchAsync(productId, Mockups with { PrintProviderId = 3 }, default);

        result.Value!.Setup.PrintProviderId.Should().Be(3);
        result.Value.Setup.Sizes.Should().Equal("M", "L");
        printify.Verify(x => x.SearchBlueprintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task MatchAsync_SeveralTemplates_TakesTheOneMostMockupsShow_AndSaysSo()
    {
        var hoodie = Template("Gildan 18500");
        Mockup(hoodie, "#000000");
        Mockup(tee, "#000000", 1);
        Mockup(tee, "#1F2A44", 2);

        var result = await Create().MatchAsync(productId, Mockups, default);

        result.Value!.TemplateName.Should().Be("Bella+Canvas 3001");
        result.Value.Warnings.Should().ContainSingle(warning => warning.Contains("several products"));
    }

    [TestMethod]
    public async Task MatchAsync_MockupsOfTheGarmentsOtherSides_AreNotAnotherProduct()
    {
        // The back photo is a template of its own, linked to the front; made first and more often, it still is the tee.
        var back = Template("Bella+Canvas 3001 · Back");
        back.PrintAreaConfig = $$"""{"view":"back","mainTemplateId":"{{tee.Id}}"}""";
        Mockup(back, "#000000");
        Mockup(back, "#1F2A44", 1);
        Mockup(back, "#1F2A44", 2);
        Mockup(tee, "#000000", 3);
        Mockup(tee, "#1F2A44", 4);

        var result = await Create().MatchAsync(productId, Mockups, default);

        result.Value!.TemplateName.Should().Be("Bella+Canvas 3001");
        result.Value.Setup.Colors.Should().Equal("Black", "Navy");
        result.Value.Warnings.Should().BeEmpty();
    }
}
