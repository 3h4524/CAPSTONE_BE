using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class ModelPhotoServiceTests
{
    private static readonly Guid Seller = Guid.NewGuid();
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] SelectionBytes = [0x89, 0x50, 0x4E, 0x47, 7, 7];

    private readonly List<MockupTemplate> _rows = [];
    private readonly Mock<IRepository<MockupTemplate>> _templates = new();
    private readonly Mock<IPublicImageService> _images = new();
    private readonly Mock<IMockupMapService> _maps = new();
    private GarmentMaskStats _stats = new(0.12, 0.88);

    public ModelPhotoServiceTests()
    {
        _templates.Setup(x => x.Query()).Returns(() => _rows.AsQueryable().BuildMock());
        _templates.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _rows.SingleOrDefault(x => x.Id == (Guid)id));
        _templates.Setup(x => x.AddAsync(It.IsAny<MockupTemplate>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((MockupTemplate template, bool _, CancellationToken _) => _rows.Add(template)).Returns(Task.CompletedTask);
        _templates.Setup(x => x.UpdateAsync(It.IsAny<MockupTemplate>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _images.Setup(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadFileDto _, string key, CancellationToken _) => new PublicImageUploadResult($"https://res.cloudinary.com/demo/image/upload/v1/{key}.jpg", 1000, 1500));

        _maps.SetupGet(x => x.IsAvailable).Returns(true);
        _maps.Setup(x => x.PrepareModelPhotoAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PreparedBasePhoto([9], [1], _stats, 1000, 1500));
        _maps.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(JpegBytes);
        _maps.Setup(x => x.StoreHelpersAsync(It.IsAny<string>(), It.IsAny<PreparedBasePhoto>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [TestMethod]
    public async Task CreateAsync_SellersPhoto_IsTheirOwnModelPhotoWhoseGarmentIsAlwaysRecolored()
    {
        var result = await Service().CreateAsync(Request(), system: false);

        result.IsSuccess.Should().BeTrue();
        var stored = _rows.Should().ContainSingle().Subject;
        stored.Kind.Should().Be(MockupRules.ModelKind);
        stored.UserId.Should().Be(Seller);
        stored.IsSystemTemplate.Should().BeFalse();
        stored.AllowRecolor.Should().BeTrue();
        stored.GarmentIsLight.Should().BeTrue();
        MockupRules.HasCurrentMaps(stored).Should().BeTrue();
        (stored.OutputWidthPx, stored.OutputHeightPx).Should().Be((1000, 1500));
        result.Value.Kind.Should().Be(MockupRules.ModelKind);
        result.Value.IsMine.Should().BeTrue();
        _maps.Verify(x => x.PrepareModelPhotoAsync(JpegBytes, SelectionBytes, It.IsAny<CancellationToken>()), Times.Once);
        _maps.Verify(x => x.StoreHelpersAsync(MockupRules.HelperKeyPrefix(stored.Id, stored.PrintMapsVersion!.Value), It.IsAny<PreparedBasePhoto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_AutomaticPlacement_IsKeptWithThePrintArea()
    {
        var result = await Service().CreateAsync(Request() with { AutoPlacement = true }, system: false);

        var stored = _rows.Single();
        MockupRules.IsAutomaticPlacement(stored.PrintAreaConfig).Should().BeTrue();
        MockupRules.ParsePrintArea(stored.PrintAreaConfig).Should().Be(new MockupPosition(300, 400, 400, 500));
        MockupRules.IsAutomaticPlacement(result.Value.PrintAreaConfig).Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateAsync_PrintAreaMarkedByItsCorners_KeepsThemAndTheBoxAroundThem()
    {
        MockupPoint[] quad = [new(300, 400), new(700, 420), new(690, 900), new(310, 880)];

        var result = await Service().CreateAsync(Request() with { PrintQuad = quad }, system: false);

        result.IsSuccess.Should().BeTrue();
        var stored = _rows.Single();
        MockupRules.ParsePrintQuad(stored.PrintAreaConfig).Should().BeEquivalentTo(quad, options => options.WithStrictOrdering());
        MockupRules.ParsePrintArea(stored.PrintAreaConfig).Should().Be(new MockupPosition(300, 400, 400, 500));
    }

    [TestMethod]
    public async Task CreateAsync_CornersThatCross_AreRefused()
    {
        MockupPoint[] crossing = [new(300, 400), new(690, 900), new(700, 420), new(310, 880)];

        var result = await Service().CreateAsync(Request() with { PrintQuad = crossing }, system: false);

        result.Error.Type.Should().Be(ErrorType.Validation);
        _rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateAsync_AutomaticPlacement_IgnoresCorners()
    {
        MockupPoint[] crossing = [new(300, 400), new(690, 900), new(700, 420), new(310, 880)];

        var result = await Service().CreateAsync(Request() with { AutoPlacement = true, PrintQuad = crossing }, system: false);

        result.IsSuccess.Should().BeTrue();
        MockupRules.ParsePrintQuad(_rows.Single().PrintAreaConfig).Should().BeNull();
    }

    [TestMethod]
    public async Task UpdateAsync_TurningAutomaticPlacementOff_PlacesDesignsInThePrintArea()
    {
        var own = AddModelPhoto(Seller);
        own.PrintAreaConfig = MockupRules.SerializePrintArea(new MockupPosition(300, 400, 400, 500), automatic: true);

        await Service().UpdateAsync(own.Id, Request(photo: false, selection: false) with { AutoPlacement = false }, system: false);

        MockupRules.IsAutomaticPlacement(own.PrintAreaConfig).Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateAsync_ForTheLibrary_BelongsToNoSeller()
    {
        var result = await Service().CreateAsync(Request(), system: true);

        result.IsSuccess.Should().BeTrue();
        var stored = _rows.Single();
        stored.UserId.Should().BeNull();
        stored.IsSystemTemplate.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateAsync_WithoutAGarmentSelection_IsRefused()
    {
        var result = await Service().CreateAsync(Request() with { GarmentSelection = null }, system: false);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        _rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateAsync_DarkGarment_IsRefusedBeforeAnythingIsStored()
    {
        _stats = new GarmentMaskStats(0.12, 0.4);

        var result = await Service().CreateAsync(Request(), system: false);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BatchMockups.ModelPhotoUnusable");
        result.Error.Message.Should().Contain("too dark");
        _images.Verify(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateAsync_PrintAreaOutsideThePhoto_IsRefusedAndTheUploadRemoved()
    {
        _maps.Setup(x => x.PrepareModelPhotoAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreparedBasePhoto([9], [1], _stats));

        var result = await Service().CreateAsync(Request() with { X = 900, Width = 300 }, system: false);

        result.IsFailure.Should().BeTrue();
        _images.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateAsync_AnotherSellersPhotoOrTheLibrarys_IsForbiddenToASeller()
    {
        var foreign = AddModelPhoto(Guid.NewGuid());
        var library = AddModelPhoto(null);

        (await Service().UpdateAsync(foreign.Id, Request(photo: false, selection: false), system: false)).Error.Type.Should().Be(ErrorType.Forbidden);
        (await Service().UpdateAsync(library.Id, Request(photo: false, selection: false), system: false)).Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [TestMethod]
    public async Task UpdateAsync_ASellersPhoto_IsNotTheLibrarysToChange()
    {
        var own = AddModelPhoto(Seller);

        var result = await Service().UpdateAsync(own.Id, Request(photo: false, selection: false), system: true);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [TestMethod]
    public async Task UpdateAsync_GarmentTemplate_IsNotAModelPhoto()
    {
        var garment = AddModelPhoto(Seller);
        garment.Kind = MockupRules.GarmentKind;

        var result = await Service().UpdateAsync(garment.Id, Request(photo: false, selection: false), system: false);

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [TestMethod]
    public async Task UpdateAsync_NewPhotoWithoutANewSelection_IsRefused()
    {
        var own = AddModelPhoto(Seller);

        var result = await Service().UpdateAsync(own.Id, Request(selection: false), system: false);

        result.Error.Type.Should().Be(ErrorType.Validation);
        _images.Verify(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_NewSelectionOnly_AnalyzesTheStoredPhotoAgain()
    {
        var own = AddModelPhoto(Seller);

        var result = await Service().UpdateAsync(own.Id, Request(photo: false), system: false);

        result.IsSuccess.Should().BeTrue();
        _maps.Verify(x => x.DownloadAsync(own.BaseImageUrl, It.IsAny<CancellationToken>()), Times.Once);
        _maps.Verify(x => x.PrepareModelPhotoAsync(JpegBytes, SelectionBytes, It.IsAny<CancellationToken>()), Times.Once);
        own.PrintMapsVersion.Should().NotBe(1);
        MockupRules.HasCurrentMaps(own).Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateAsync_FindGarment_FindsItOnThePhotoWithoutASelection()
    {
        var result = await Service().CreateAsync(Request(selection: false) with { FindGarment = true }, system: false);

        result.IsSuccess.Should().BeTrue();
        _maps.Verify(x => x.PrepareModelPhotoAsync(JpegBytes, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_FindGarmentWhenThereIsNone_IsRefusedBeforeAnythingIsStored()
    {
        _maps.Setup(x => x.PrepareModelPhotoAsync(It.IsAny<byte[]>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PreparedBasePhoto?)null);

        var result = await Service().CreateAsync(Request(selection: false) with { FindGarment = true }, system: false);

        result.Error.Code.Should().Be("BatchMockups.GarmentNotFound");
        _images.Verify(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateAsync_FindGarmentOnly_FindsItAgainOnTheStoredPhoto()
    {
        var own = AddModelPhoto(Seller);

        var result = await Service().UpdateAsync(own.Id, Request(photo: false, selection: false) with { FindGarment = true }, system: false);

        result.IsSuccess.Should().BeTrue();
        _maps.Verify(x => x.DownloadAsync(own.BaseImageUrl, It.IsAny<CancellationToken>()), Times.Once);
        _maps.Verify(x => x.PrepareModelPhotoAsync(JpegBytes, null, It.IsAny<CancellationToken>()), Times.Once);
        own.PrintMapsVersion.Should().NotBe(1);
    }

    [TestMethod]
    public async Task UpdateAsync_NameAndPrintAreaOnly_KeepsTheGarment()
    {
        var own = AddModelPhoto(Seller);

        var result = await Service().UpdateAsync(own.Id, Request(photo: false, selection: false) with { Name = "Renamed" }, system: false);

        result.IsSuccess.Should().BeTrue();
        own.Name.Should().Be("Renamed");
        own.PrintMapsVersion.Should().Be(1);
        _maps.Verify(x => x.PrepareModelPhotoAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListAsync_TheLibraryAndTheSellersOwn_ButNoGarmentTemplatesOrOtherSellersPhotos()
    {
        var library = AddModelPhoto(null);
        var own = AddModelPhoto(Seller);
        AddModelPhoto(Guid.NewGuid());
        AddModelPhoto(Seller).Kind = MockupRules.GarmentKind;
        AddModelPhoto(Seller).IsActive = false;

        var result = await Service().ListAsync("TSHIRT");

        result.Value.Select(x => x.Id).Should().BeEquivalentTo([library.Id, own.Id]);
    }

    [TestMethod]
    public async Task ListSystemAsync_OnlyTheLibrary()
    {
        var library = AddModelPhoto(null);
        AddModelPhoto(Seller);

        var result = await Service().ListSystemAsync();

        result.Value.Select(x => x.Id).Should().Equal(library.Id);
    }

    [TestMethod]
    public async Task CreateAsync_KeepsWhoIsInThePhoto_AndOnePersonWhenNotTold()
    {
        var family = await Service().CreateAsync(Request() with { Attributes = new("Group", GroupType: "Family") }, system: false);
        var untold = await Service().CreateAsync(Request() with { Name = "Second model" }, system: false);

        family.Value.Attributes.Should().Be(new ModelAttributesDto("group", null, null, "family"));
        _rows.Single(x => x.Id == family.Value.Id).GroupType.Should().Be("family");
        untold.Value.Attributes.Should().Be(new ModelAttributesDto("single"));
    }

    [TestMethod]
    public async Task CreateAsync_AttributesThatDoNotGoTogether_AreRefused()
    {
        var result = await Service().CreateAsync(Request() with { Attributes = new("single", "adult", null) }, system: false);

        result.Error.Type.Should().Be(ErrorType.Validation);
        _rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ListAsync_Filter_KeepsOnlyThePhotosOfThePeopleAskedFor()
    {
        var girl = AddModelPhoto(null);
        ModelPeople.Apply(girl, new("single", "child", "female"));
        var man = AddModelPhoto(Seller);
        ModelPeople.Apply(man, new("single", "adult", "male"));
        var couple = AddModelPhoto(null);
        ModelPeople.Apply(couple, new("group", GroupType: "couple"));

        (await Service().ListAsync(null, new ModelPhotoFilter(People: "single"))).Value.Select(x => x.Id).Should().BeEquivalentTo([girl.Id, man.Id]);
        (await Service().ListAsync(null, new ModelPhotoFilter(AgeGroup: "Child", Gender: "female"))).Value.Select(x => x.Id).Should().Equal(girl.Id);
        (await Service().ListAsync(null, new ModelPhotoFilter(People: "group", GroupType: "couple"))).Value.Select(x => x.Id).Should().Equal(couple.Id);
        (await Service().ListSystemAsync(new ModelPhotoFilter(Gender: "male"))).Value.Should().BeEmpty();
        (await Service().ListAsync(null, new ModelPhotoFilter(" ", "", null, null))).Value.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task DeleteAsync_LibraryPhoto_IsTakenOutOfUse()
    {
        var library = AddModelPhoto(null);

        var result = await Service().DeleteAsync(library.Id, system: true);

        result.IsSuccess.Should().BeTrue();
        library.IsActive.Should().BeFalse();
    }

    private ModelPhotoService Service()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(Seller);
        var compositor = new Mock<IMockupCompositor>();
        compositor.Setup(x => x.BuildAssetUrl(It.IsAny<string>(), It.IsAny<string>())).Returns("https://cdn/mask.png");
        return new ModelPhotoService(currentUser.Object, _templates.Object, _images.Object, compositor.Object, _maps.Object,
            new SaveModelPhotoValidator(), TimeProvider.System, NullLogger<ModelPhotoService>.Instance);
    }

    private static SaveModelPhotoRequestDto Request(bool photo = true, bool selection = true) =>
        new("Studio model", "tshirt", 300, 400, 400, 500,
            photo ? new UploadFileDto("model.jpg", "image/jpeg", JpegBytes.Length, new MemoryStream(JpegBytes)) : null,
            selection ? new UploadFileDto("selection.png", "image/png", SelectionBytes.Length, new MemoryStream(SelectionBytes)) : null);

    private MockupTemplate AddModelPhoto(Guid? owner)
    {
        var id = Guid.NewGuid();
        var url = $"https://res.cloudinary.com/demo/image/upload/v1/model-{id:N}.jpg";
        var template = new MockupTemplate
        {
            Id = id, UserId = owner, Name = $"Model {id:N}", ProductType = "tshirt", BaseImageUrl = url,
            PrintAreaConfig = MockupRules.SerializePrintArea(new MockupPosition(300, 400, 400, 500)), OutputWidthPx = 1000, OutputHeightPx = 1500,
            IsSystemTemplate = owner is null, IsActive = true, Kind = MockupRules.ModelKind, AllowRecolor = true, GarmentIsLight = true,
            PrintMapsSourceUrl = url, PrintMapsVersion = 1
        };
        _rows.Add(template);
        return template;
    }
}
