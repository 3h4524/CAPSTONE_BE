using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Printify;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class PrintifyColorMapServiceTests
{
    private const long Blueprint = 12;

    private readonly List<PrintifyColorMap> rows = [];
    private readonly Mock<IRepository<PrintifyColorMap>> colors = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();

    private PrintifyColorMapService Service()
    {
        colors.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        colors.Setup(x => x.AddAsync(It.IsAny<PrintifyColorMap>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<PrintifyColorMap, bool, CancellationToken>((row, _, _) => rows.Add(row));
        var printify = new Mock<IPrintifyClient>();
        printify.Setup(x => x.GetBlueprintProvidersAsync(It.IsAny<string>(), Blueprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PrintifyProvider(99, "Printify Choice", null, null)]);
        printify.Setup(x => x.GetProvidersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        printify.Setup(x => x.GetVariantsAsync(It.IsAny<string>(), Blueprint, 99, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PrintifyCatalogVariant(1, "Black / M", "Black", "M", []), new PrintifyCatalogVariant(2, "Navy / M", "Navy", "M", [])]);
        var token = new Mock<IPrintifyAdminToken>();
        token.SetupGet(x => x.Token).Returns("platform-token");
        var catalog = new PrintifyAdminCatalog(printify.Object, token.Object, new TestMemoryCache(), NullLogger<PrintifyAdminCatalog>.Instance);
        return new PrintifyColorMapService(colors.Object, catalog, token.Object, unitOfWork.Object, TimeProvider.System);
    }

    private PrintifyColorMap Row(string name, string hex, long? blueprintId)
    {
        var row = new PrintifyColorMap { Id = Guid.NewGuid(), PrintifyName = name, Hex = hex, BlueprintId = blueprintId, CreatedAt = DateTime.UtcNow };
        rows.Add(row);
        return row;
    }

    [TestMethod]
    public async Task ListAsync_TheBlueprintsOwnColorWinsOverTheSharedOne()
    {
        Row("Black", "#111111", null);
        Row("Navy", "#252A3F", null);
        Row("Black", "#232424", Blueprint);
        Row("Red", "#AA0000", 99);

        var result = await Service().ListAsync(Blueprint);

        result.Value.Select(row => (row.Name, row.Hex, row.Shared)).Should().Equal(("Black", "#232424", false), ("Navy", "#252A3F", true));
    }

    [TestMethod]
    public async Task SaveAsync_AddsThenChangesTheBlueprintsColorByName()
    {
        var added = await Service().SaveAsync(new SavePrintifyColorRequestDto(Blueprint, "black", "#232424"));
        var changed = await Service().SaveAsync(new SavePrintifyColorRequestDto(Blueprint, "Black", "#1a1b1c"));

        added.Value.Name.Should().Be("Black");
        changed.Value.Hex.Should().Be("#1A1B1C");
        rows.Should().ContainSingle();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task SaveAsync_NameNotInTheCatalog_IsRefused()
    {
        var result = await Service().SaveAsync(new SavePrintifyColorRequestDto(Blueprint, "Mauve", "#AA8899"));

        result.Error.Message.Should().Contain("Mauve");
        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SaveAsync_HexOfAnotherColor_IsRefused()
    {
        Row("Black", "#232424", Blueprint);

        var result = await Service().SaveAsync(new SavePrintifyColorRequestDto(Blueprint, "Navy", "#232424"));

        result.Error.Code.Should().Be("PrintifyTemplates.HexTaken");
    }

    [TestMethod]
    public async Task SaveAsync_HexNotWrittenAsRrggbb_IsRefused()
    {
        var result = await Service().SaveAsync(new SavePrintifyColorRequestDto(Blueprint, "Navy", "navy"));

        result.Error.Message.Should().Contain("#RRGGBB");
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTheBlueprintsRowButNeverASharedOne()
    {
        var own = Row("Black", "#232424", Blueprint);
        var shared = Row("Navy", "#252A3F", null);
        colors.Setup(x => x.GetByIdAsync(own.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        colors.Setup(x => x.GetByIdAsync(shared.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shared);

        var removed = await Service().DeleteAsync(own.Id);
        var kept = await Service().DeleteAsync(shared.Id);

        removed.IsSuccess.Should().BeTrue();
        kept.IsFailure.Should().BeTrue();
        colors.Verify(x => x.RemoveAsync(own, true, It.IsAny<CancellationToken>()), Times.Once);
        colors.Verify(x => x.RemoveAsync(shared, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
