using System.Reflection;
using APCS.Api.Contracts.BatchMockups;
using APCS.Api.Controllers;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class ModelPhotosControllerTests
{
    private readonly Mock<IModelPhotoService> _service = new();
    private readonly Mock<IModelPhotoGenerationService> _generation = new();

    private ModelPhotosController Seller() => new(_service.Object, _generation.Object);
    private AdminModelPhotosController Admin() => new(_service.Object, _generation.Object);

    private static MockupTemplateResponseDto Photo(Guid id) =>
        new(id, "Man in a white tee", "tshirt", "https://example.com/man.jpg", null, "{}", 1200, 1500, 0,
            IsSystemTemplate: false, IsMine: true, RealisticPrintReady: true, AllowRecolor: true, GarmentMaskUrl: null,
            GarmentColor: "#FFFFFF", Kind: MockupRules.ModelKind);

    private static IFormFile File(string name) =>
        new FormFile(new MemoryStream([1, 2, 3]), 0, 3, name, $"{name}.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };

    private static SaveModelPhotoForm Form() => new()
    {
        Name = "Man in a white tee", ProductType = "tshirt", X = 10, Y = 20, Width = 300, Height = 400,
        Photo = File("photo"), GarmentSelection = File("selection"), People = ModelPeople.Single, AgeGroup = "adult", Gender = "male",
        PrintQuad = "0,0, 10,0, 10,10, 0,10",
    };

    [TestMethod]
    public void Seller_IsForSellers_AndAdmin_IsForAdmins()
    {
        typeof(ModelPhotosController).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(AuthConstants.UserRole);
        typeof(AdminModelPhotosController).GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Admin,Super Admin");
    }

    [TestMethod]
    public async Task List_PassesTheProductTypeAndWhoIsInThePhotos()
    {
        var id = Guid.NewGuid();
        _service.Setup(s => s.ListAsync("tshirt", new ModelPhotoFilter("single", "child", null, null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MockupTemplateResponseDto>>([Photo(id)]));

        var result = await Seller().List("tshirt", new ModelPhotoQuery { People = "single", AgeGroup = "child" }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.As<IReadOnlyList<MockupTemplateResponseDto>>()
            .Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [TestMethod]
    public async Task AdminList_ListsTheSharedLibrary()
    {
        _service.Setup(s => s.ListSystemAsync(new ModelPhotoFilter(null, null, null, "family"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MockupTemplateResponseDto>>([]));

        var result = await Admin().List(new ModelPhotoQuery { GroupType = "family" }, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [TestMethod]
    public async Task Generate_SellerKeepsItAsTheirOwn_AdminPutsItInTheLibrary()
    {
        var request = new GenerateModelPhotoRequestDto("tshirt", new ModelAttributesDto("single", "adult", "female", null));
        _generation.Setup(s => s.GenerateAsync(request, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Photo(Guid.NewGuid())));
        _generation.Setup(s => s.GenerateAsync(request, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupTemplateResponseDto>(Error.Conflict("MissingApiKey", "Connect an Ideogram key.")));

        (await Seller().Generate(request, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await Admin().Generate(request, CancellationToken.None)).Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [TestMethod]
    public async Task Preview_SendsTheUploadedPhoto()
    {
        UploadFileDto? sent = null;
        _service.Setup(s => s.PreviewAsync(It.IsAny<UploadFileDto?>(), It.IsAny<CancellationToken>()))
            .Callback<UploadFileDto?, CancellationToken>((photo, _) => sent = photo)
            .ReturnsAsync(Result.Success(new GarmentMaskPreviewResponseDto(true, null, "data:image/png;base64,AA==")));

        var result = await Seller().Preview(File("photo"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        sent!.FileName.Should().Be("photo.png");
    }

    [TestMethod]
    public async Task AdminPreview_WithoutAPhoto_PassesTheServicesAnswerOn()
    {
        _service.Setup(s => s.PreviewAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<GarmentMaskPreviewResponseDto>(Error.Validation("Choose a photo.")));

        var result = await Admin().Preview(null, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task Create_SendsTheFormAsARequest_ForTheSellersOwnPhoto()
    {
        SaveModelPhotoRequestDto? sent = null;
        _service.Setup(s => s.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), false, It.IsAny<CancellationToken>()))
            .Callback<SaveModelPhotoRequestDto, bool, CancellationToken>((request, _, _) => sent = request)
            .ReturnsAsync(Result.Success(Photo(Guid.NewGuid())));

        var result = await Seller().Create(Form(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        sent!.Name.Should().Be("Man in a white tee");
        sent.Photo.Should().NotBeNull();
        sent.GarmentSelection.Should().NotBeNull();
        sent.Attributes.Should().Be(new ModelAttributesDto("single", "adult", "male", null));
        sent.PrintQuad.Should().HaveCount(4);
    }

    [TestMethod]
    public async Task AdminCreate_PutsThePhotoInTheLibrary()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Photo(Guid.NewGuid())));

        (await Admin().Create(Form(), CancellationToken.None)).Should().BeOfType<OkObjectResult>();
    }

    [TestMethod]
    public async Task Update_UnknownPhoto_ReturnsNotFound_ForSellerAndAdmin()
    {
        var id = Guid.NewGuid();
        _service.Setup(s => s.UpdateAsync(id, It.IsAny<SaveModelPhotoRequestDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupTemplateResponseDto>(Error.NotFound("ModelPhotoNotFound", "Model photo not found.")));

        (await Seller().Update(id, new SaveModelPhotoForm(), CancellationToken.None)).Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        (await Admin().Update(id, new SaveModelPhotoForm(), CancellationToken.None)).Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        _service.Verify(s => s.UpdateAsync(id, It.IsAny<SaveModelPhotoRequestDto>(), false, It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.UpdateAsync(id, It.IsAny<SaveModelPhotoRequestDto>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Delete_ReturnsNoContent_ForSellerAndAdmin()
    {
        var id = Guid.NewGuid();
        _service.Setup(s => s.DeleteAsync(id, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        (await Seller().Delete(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        (await Admin().Delete(id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.DeleteAsync(id, false, It.IsAny<CancellationToken>()), Times.Once);
        _service.Verify(s => s.DeleteAsync(id, true, It.IsAny<CancellationToken>()), Times.Once);
    }
}

[TestClass]
public sealed class ModelPhotoMockupsControllerTests
{
    private readonly Mock<IModelPhotoMockupService> _service = new();

    [TestMethod]
    public async Task Generate_Made_ReturnsCreated()
    {
        var productId = Guid.NewGuid();
        var request = new ModelPhotoMockupRequest(Guid.NewGuid(), Guid.NewGuid());
        var made = new MockupResponse(Guid.NewGuid(), productId, "uploaded", "Lifestyle", null, null, 1, "pending", null, 800, 1000,
            new MockupRegions(), "https://example.com/made.png", []);
        _service.Setup(s => s.GenerateAsync(productId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(made));

        var result = await new ModelPhotoMockupsController(_service.Object).Generate(productId, request, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
    }

    [TestMethod]
    public async Task Generate_UnknownMockup_ReturnsNotFound()
    {
        _service.Setup(s => s.GenerateAsync(It.IsAny<Guid>(), It.IsAny<ModelPhotoMockupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupResponse>(Error.NotFound("MockupNotFound", "Product or mockup not found.")));

        var result = await new ModelPhotoMockupsController(_service.Object)
            .Generate(Guid.NewGuid(), new ModelPhotoMockupRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task Preview_ReturnsTheImageUrl()
    {
        _service.Setup(s => s.PreviewAsync(It.IsAny<Guid>(), It.IsAny<ModelPhotoMockupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ModelPhotoPreviewResponse("https://example.com/preview.png")));

        var result = await new ModelPhotoMockupsController(_service.Object)
            .Preview(Guid.NewGuid(), new ModelPhotoMockupRequest(Guid.NewGuid(), Guid.NewGuid(), "#000000"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(new ModelPhotoPreviewResponse("https://example.com/preview.png"));
    }
}
