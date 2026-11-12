using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.MockupTemplates;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class AdminMockupTemplateServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private readonly List<MockupTemplate> rows = [];
    private readonly Mock<IMockupTemplatePrintifyLinkRepository> links = new();

    private AdminMockupTemplateService Service()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(AdminId);
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        var templates = new Mock<IRepository<MockupTemplate>>();
        templates.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        links.Setup(x => x.GetAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TemplatePrintifyLink>());
        return new AdminMockupTemplateService(user.Object, templates.Object, links.Object);
    }

    private MockupTemplate Template(string name, bool system = true)
    {
        var template = new MockupTemplate
        {
            Id = Guid.NewGuid(), Name = name, ProductType = "tshirt", Kind = "garment", BaseImageUrl = "https://cdn.test/t.png",
            PrintAreaConfig = "{}", IsSystemTemplate = system, IsActive = true, UserId = system ? null : Guid.NewGuid(),
        };
        rows.Add(template);
        return template;
    }

    private static LinkMockupTemplatePrintifyRequestDto Bella(params string[] sizes) =>
        new(12, " Unisex Jersey Short Sleeve Tee ", "https://images.printify.com/12.png", 99, "Monster Digital", sizes);

    [TestMethod]
    public async Task ListAsync_ReturnsOnlySystemTemplatesWithTheirLinks()
    {
        var service = Service();
        var tee = Template("Classic Tee - Flat Lay");
        Template("My own tee", system: false);
        links.Setup(x => x.GetAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TemplatePrintifyLink>
            {
                [tee.Id] = new(tee.Id, 12, "Unisex Jersey Short Sleeve Tee", null, 99, "Monster Digital", ["S", "M"]),
            });

        var result = await service.ListAsync();

        result.Value.Should().ContainSingle().Which.Printify!.BlueprintId.Should().Be(12);
    }

    [TestMethod]
    public async Task LinkPrintifyAsync_SystemTemplate_SavesTheTrimmedLinkByTheAdmin()
    {
        var service = Service();
        var tee = Template("Classic Tee - Flat Lay");

        var result = await service.LinkPrintifyAsync(tee.Id, Bella(" S ", "M", "m", ""));

        result.IsSuccess.Should().BeTrue();
        result.Value.Printify!.BlueprintTitle.Should().Be("Unisex Jersey Short Sleeve Tee");
        links.Verify(x => x.SaveAsync(It.Is<TemplatePrintifyLink>(link =>
            link.TemplateId == tee.Id && link.BlueprintId == 12 && link.DefaultSizes.SequenceEqual(new[] { "S", "M" })), AdminId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task LinkPrintifyAsync_SellersOwnTemplate_IsNotFound()
    {
        var service = Service();
        var own = Template("My own tee", system: false);

        var result = await service.LinkPrintifyAsync(own.Id, Bella("S"));

        result.Error.Type.Should().Be(ErrorType.NotFound);
        links.Verify(x => x.SaveAsync(It.IsAny<TemplatePrintifyLink>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow(0L, "https://images.printify.com/12.png")]
    [DataRow(12L, "http://images.printify.com/12.png")]
    public async Task LinkPrintifyAsync_InvalidRequest_IsAValidationError(long blueprintId, string imageUrl)
    {
        var service = Service();
        var tee = Template("Classic Tee - Flat Lay");

        var result = await service.LinkPrintifyAsync(tee.Id, Bella("S") with { BlueprintId = blueprintId, BlueprintImageUrl = imageUrl });

        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task UnlinkPrintifyAsync_SystemTemplate_RemovesTheLink()
    {
        var service = Service();
        var tee = Template("Classic Tee - Flat Lay");

        var result = await service.UnlinkPrintifyAsync(tee.Id);

        result.IsSuccess.Should().BeTrue();
        links.Verify(x => x.DeleteAsync(tee.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
