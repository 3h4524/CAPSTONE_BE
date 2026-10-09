using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class MockupTemplateServiceTests
{
    [TestMethod]
    public async Task ListAsync_WhenProductTypeGiven_ReturnsOnlyMatchingActive()
    {
        var repository = TemplateRepository(
            new MockupTemplate { Id = Guid.NewGuid(), Name = "Tee A", ProductType = "tshirt", BaseImageUrl = "https://x/a.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true, UsageCount = 1 },
            new MockupTemplate { Id = Guid.NewGuid(), Name = "Mug A", ProductType = "mug", BaseImageUrl = "https://x/b.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true, UsageCount = 9 },
            new MockupTemplate { Id = Guid.NewGuid(), Name = "Tee Off", ProductType = "tshirt", BaseImageUrl = "https://x/c.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = false, UsageCount = 99 });

        var result = await CreateService(Guid.NewGuid(), templates: repository).ListAsync("TSHIRT");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Name.Should().Be("Tee A");
    }

    [TestMethod]
    public async Task ApplyAsync_WhenValid_StoresIdsInBatchConfig()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var batch = new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "Draft", Config = "{\"other\":\"keep\"}" };
        var batches = MockBatches(batch);
        var templates = TemplateRepository(
            new MockupTemplate { Id = templateId, Name = "Tee A", ProductType = "tshirt", BaseImageUrl = "https://x/a.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true });
        var rows = MockRows(new BatchJobProduct { Id = Guid.NewGuid(), BatchJobId = batchId, ProductId = productId, Status = "Pending" });
        var products = MockProducts(new Product { Id = productId, UserId = userId, Name = "P", ProductType = "TSHIRT", InputDescription = "d", ProcessingStatus = "pending" });

        var result = await CreateService(userId, templates, batches, rows, products)
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([templateId]));

        result.IsSuccess.Should().BeTrue();
        result.Value.TemplateIds.Should().ContainSingle().Which.Should().Be(templateId);
        batch.Config.Should().Contain("mockupTemplateIds").And.Contain("other");
    }

    [TestMethod]
    public async Task ApplyAsync_WhenBatchOfAnotherSeller_ReturnsNotFound()
    {
        var batches = MockBatches(new BatchJob { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "B", Status = "Draft", Config = "{}" });

        var result = await CreateService(Guid.NewGuid(), batches: batches)
            .ApplyAsync(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto([Guid.NewGuid()]));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.NotFound);
    }

    [TestMethod]
    public async Task ApplyAsync_WhileJobIsGenerating_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "Queued", Config = "{}" });

        var result = await CreateService(userId, batches: batches)
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([Guid.NewGuid()]));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.JobGenerating");
    }

    [TestMethod]
    public async Task ApplyAsync_AfterJobCompleted_StoresSelection()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var batch = new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "completed", Config = "{}" };
        var templates = TemplateRepository(new MockupTemplate
        {
            Id = templateId, Name = "Tee", ProductType = "tshirt", BaseImageUrl = "https://x/a.jpg",
            PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
        });

        var result = await CreateService(userId, templates: templates, batches: MockBatches(batch), rows: MockRows(), products: MockProducts())
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([templateId]));

        result.IsSuccess.Should().BeTrue();
        batch.Config.Should().Contain(templateId.ToString());
    }

    [TestMethod]
    public async Task ApplyAsync_WhenEmptySelection_ReturnsFailure()
    {
        var result = await CreateService(Guid.NewGuid())
            .ApplyAsync(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto([]));

        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetSelectionAsync_WhenOwnedBatch_ReturnsStoredIds()
    {
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob
        {
            Id = batchId,
            UserId = userId,
            Name = "B",
            Status = "draft",
            Config = $"{{\"mockupTemplateIds\":[\"{templateId:D}\"]}}"
        });

        var result = await CreateService(userId, batches: batches).GetSelectionAsync(batchId);

        result.IsSuccess.Should().BeTrue();
        result.Value.TemplateIds.Should().ContainSingle().Which.Should().Be(templateId);
    }

    [TestMethod]
    public async Task GetSelectionAsync_WhenUnauthenticated_ReturnsFailure()
    {
        var result = await CreateService(null).GetSelectionAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task ApplyAsync_WhenTemplateInactive_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "draft", Config = "{}" });
        var templates = TemplateRepository(
            new MockupTemplate { Id = templateId, Name = "Old", ProductType = "mug", BaseImageUrl = "https://x/o.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = false });

        var result = await CreateService(userId, templates: templates, batches: batches)
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([templateId]));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.NotFound);
    }

    [TestMethod]
    public async Task ApplyAsync_WhenTooManyIds_ReturnsFailure()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();

        var result = await CreateService(Guid.NewGuid())
            .ApplyAsync(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto(ids));

        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task ApplyAsync_WhenIncompatibleType_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "draft", Config = "{}" });
        var templates = TemplateRepository(
            new MockupTemplate { Id = templateId, Name = "Mug A", ProductType = "mug", BaseImageUrl = "https://x/m.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true });
        var rows = MockRows(new BatchJobProduct { Id = Guid.NewGuid(), BatchJobId = batchId, ProductId = productId, Status = "pending" });
        var products = MockProducts(new Product { Id = productId, UserId = userId, Name = "Tee", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" });

        var result = await CreateService(userId, templates: templates, batches: batches, rows: rows, products: products)
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([templateId]));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
    }

    // ---- CreateAsync ----

    [TestMethod]
    public async Task CreateAsync_WhenValid_UploadsPhotoAndSavesTemplate()
    {
        var userId = Guid.NewGuid();
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var images = MockImages(uploadResult: new PublicImageUploadResult("https://cdn/x.jpg", 2000, 2000));

        var result = await CreateService(userId, templates: templates, images: images)
            .CreateAsync(new CreateMockupTemplateRequestDto("Classic Tee", "tshirt", 100, 100, 500, 500, SampleFile()));

        result.IsSuccess.Should().BeTrue();
        result.Value.BaseImageUrl.Should().Be("https://cdn/x.jpg");
        result.Value.IsMine.Should().BeTrue();
        added.Should().ContainSingle().Which.UserId.Should().Be(userId);
    }

    [TestMethod]
    public async Task CreateAsync_WhenNameTaken_ReturnsConflictAndDoesNotUpload()
    {
        var userId = Guid.NewGuid();
        var templates = TemplateRepository(
            new MockupTemplate { Id = Guid.NewGuid(), Name = "Classic Tee", ProductType = "tshirt", BaseImageUrl = "https://x/a.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true });
        var images = MockImages();

        var result = await CreateService(userId, templates: templates, images: images)
            .CreateAsync(new CreateMockupTemplateRequestDto("classic tee", "tshirt", 0, 0, 100, 100, SampleFile()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.DuplicateName");
        images.Verify(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_WhenPositionOutsideUploadedImage_DeletesUploadAndReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var templates = TemplateRepository();
        var images = MockImages(uploadResult: new PublicImageUploadResult("https://cdn/x.jpg", 1000, 1000));

        var result = await CreateService(userId, templates: templates, images: images)
            .CreateAsync(new CreateMockupTemplateRequestDto("Big Print", "poster", 900, 900, 500, 500, SampleFile()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(APCS.Common.Constants.ErrorCodes.Validation);
        images.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- UpdateAsync / DeleteAsync ----

    [TestMethod]
    public async Task UpdateAsync_WhenNotOwner_ReturnsForbidden()
    {
        var templateId = Guid.NewGuid();
        var templates = TemplateRepository(new MockupTemplate
        {
            Id = templateId, UserId = Guid.NewGuid(), Name = "Other's", ProductType = "mug",
            BaseImageUrl = "https://x/o.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
        });
        SetupGetById(templates, templateId);

        var result = await CreateService(Guid.NewGuid(), templates: templates)
            .UpdateAsync(templateId, new UpdateMockupTemplateRequestDto("New name", "mug", 0, 0, 100, 100, null));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Forbidden);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenOwner_DeactivatesWithoutHardDelete()
    {
        var userId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var template = new MockupTemplate
        {
            Id = templateId, UserId = userId, Name = "Mine", ProductType = "mug",
            BaseImageUrl = "https://x/m.jpg", PrintAreaConfig = "{}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
        };
        var templates = TemplateRepository(template);
        SetupGetById(templates, templateId, template);

        var result = await CreateService(userId, templates: templates).DeleteAsync(templateId);

        result.IsSuccess.Should().BeTrue();
        template.IsActive.Should().BeFalse();
        templates.Verify(x => x.RemoveAsync(It.IsAny<MockupTemplate>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- GenerateCompositeAsync ----

    [TestMethod]
    public async Task GenerateCompositeAsync_WhenNoOverride_UsesTemplateDefaultPosition()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var designImageId = Guid.NewGuid();

        var products = MockProducts(new Product { Id = productId, UserId = userId, Name = "P", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" });
        var templates = TemplateRepository(new MockupTemplate
        {
            Id = templateId, Name = "Tee", ProductType = "tshirt", BaseImageUrl = "https://cdn/base.jpg",
            PrintAreaConfig = "{\"x\":820,\"y\":640,\"width\":900,\"height\":1100}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
        });
        var designImages = MockDesignImages(new DesignImage
        {
            Id = designImageId, ProductId = productId, AiPromptId = Guid.NewGuid(), StorageKey = "design-images/p/0",
            ImageGeneratorModel = "m", StorageProvider = "cloudinary", ImageUrl = "https://cdn/design.png",
            ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png", FileSizeMb = 1, ApprovalStatus = "pending", VariationIndex = 0, GenerationTimeSeconds = 1
        });
        var mockupImages = MockRepo<MockupImage>();
        var captured = CaptureAdded(mockupImages);
        var compositor = new Mock<IMockupCompositor>();
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>()))
            .Returns<string, string, MockupPosition, MockupLayers?>((baseUrl, overlay, position, _) => $"{baseUrl}?l={overlay}&x={position.X}");

        var result = await CreateService(userId, templates: templates, products: products, designImages: designImages,
                mockupImages: mockupImages, compositor: compositor)
            .GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.MockupImageUrl.Should().Be("https://cdn/base.jpg?l=design-images/p/0&x=820");
        captured.Should().ContainSingle();
        compositor.Verify(x => x.BuildCompositeUrl("https://cdn/base.jpg", "design-images/p/0", new MockupPosition(820, 640, 900, 1100), It.IsAny<MockupLayers?>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_WithOverride_UsesOverrideNotTemplateDefault()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var designImageId = Guid.NewGuid();

        var products = MockProducts(new Product { Id = productId, UserId = userId, Name = "P", ProductType = "mug", InputDescription = "d", ProcessingStatus = "pending" });
        var templates = TemplateRepository(new MockupTemplate
        {
            Id = templateId, Name = "Mug", ProductType = "mug", BaseImageUrl = "https://cdn/base.jpg",
            PrintAreaConfig = "{\"x\":1,\"y\":1,\"width\":1,\"height\":1}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
        });
        var designImages = MockDesignImages(new DesignImage
        {
            Id = designImageId, ProductId = productId, AiPromptId = Guid.NewGuid(), StorageKey = "design-images/p/0",
            ImageGeneratorModel = "m", StorageProvider = "cloudinary", ImageUrl = "https://cdn/design.png",
            ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png", FileSizeMb = 1, ApprovalStatus = "pending", VariationIndex = 0, GenerationTimeSeconds = 1
        });
        var mockupImages = MockRepo<MockupImage>();
        CaptureAdded(mockupImages);
        var compositor = new Mock<IMockupCompositor>();
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>())).Returns("https://cdn/composed.jpg");

        var result = await CreateService(userId, templates: templates, products: products, designImages: designImages,
                mockupImages: mockupImages, compositor: compositor)
            .GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, 50, 60, 300, 400));

        result.IsSuccess.Should().BeTrue();
        compositor.Verify(x => x.BuildCompositeUrl("https://cdn/base.jpg", "design-images/p/0", new MockupPosition(50, 60, 300, 400), It.IsAny<MockupLayers?>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_WhenTemplateBaseImageHasNoUploadMarker_ReturnsConflictInsteadOfThrowing()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var designImageId = Guid.NewGuid();

        var products = MockProducts(new Product { Id = productId, UserId = userId, Name = "P", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" });
        var templates = TemplateRepository(new MockupTemplate
        {
            // Mirrors the seeded system templates, which point at a placeholder domain with no
            // real Cloudinary "/upload/" segment.
            Id = templateId, Name = "Classic Tee - Male Model", ProductType = "tshirt",
            BaseImageUrl = "https://cdn.example.com/mockups/tshirt-male-model.jpg",
            PrintAreaConfig = "{\"x\":1,\"y\":1,\"width\":1,\"height\":1}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
        });
        var designImages = MockDesignImages(new DesignImage
        {
            Id = designImageId, ProductId = productId, AiPromptId = Guid.NewGuid(), StorageKey = "design-images/p/0",
            ImageGeneratorModel = "m", StorageProvider = "cloudinary", ImageUrl = "https://cdn/design.png",
            ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png", FileSizeMb = 1, ApprovalStatus = "pending", VariationIndex = 0, GenerationTimeSeconds = 1
        });
        var compositor = new Mock<IMockupCompositor>();
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>()))
            .Throws(new InvalidOperationException("Base image URL does not contain an '/upload/' segment."));

        var result = await CreateService(userId, templates: templates, products: products, designImages: designImages, compositor: compositor)
            .GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.TemplateNotUsable");
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_WhenIncompatibleProductType_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var designImageId = Guid.NewGuid();

        var products = MockProducts(new Product { Id = productId, UserId = userId, Name = "P", ProductType = "mug", InputDescription = "d", ProcessingStatus = "pending" });
        var templates = TemplateRepository(new MockupTemplate
        {
            Id = templateId, Name = "Tee", ProductType = "tshirt", BaseImageUrl = "https://cdn/base.jpg",
            PrintAreaConfig = "{\"x\":1,\"y\":1,\"width\":1,\"height\":1}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
        });
        var designImages = MockDesignImages(new DesignImage
        {
            Id = designImageId, ProductId = productId, AiPromptId = Guid.NewGuid(), StorageKey = "design-images/p/0",
            ImageGeneratorModel = "m", StorageProvider = "cloudinary", ImageUrl = "https://cdn/design.png",
            ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png", FileSizeMb = 1, ApprovalStatus = "pending", VariationIndex = 0, GenerationTimeSeconds = 1
        });

        var result = await CreateService(userId, templates: templates, products: products, designImages: designImages)
            .GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_WhenDesignImageBelongsToAnotherSeller_ReturnsNotFound()
    {
        var productId = Guid.NewGuid();
        var designImageId = Guid.NewGuid();
        var products = MockProducts(new Product { Id = productId, UserId = Guid.NewGuid(), Name = "P", ProductType = "mug", InputDescription = "d", ProcessingStatus = "pending" });
        var designImages = MockDesignImages(new DesignImage
        {
            Id = designImageId, ProductId = productId, AiPromptId = Guid.NewGuid(), StorageKey = "design-images/p/0",
            ImageGeneratorModel = "m", StorageProvider = "cloudinary", ImageUrl = "https://cdn/design.png",
            ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png", FileSizeMb = 1, ApprovalStatus = "pending", VariationIndex = 0, GenerationTimeSeconds = 1
        });

        var result = await CreateService(Guid.NewGuid(), products: products, designImages: designImages)
            .GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(Guid.NewGuid(), null, null, null, null));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.NotFound);
    }

    // ---- GenerateAllAsync ----

    [TestMethod]
    public async Task GenerateAllAsync_WhenBatchStillDraft_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob { Id = batchJobId, UserId = userId, Name = "B", Status = "draft", Config = "{}" });

        var result = await CreateService(userId, batches: batches).GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.JobNotReady");
    }

    [TestMethod]
    public async Task GenerateAllAsync_WhenNoTemplatesSelected_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob { Id = batchJobId, UserId = userId, Name = "B", Status = "completed", Config = "{}" });

        var result = await CreateService(userId, batches: batches).GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.NoTemplatesSelected");
    }

    [TestMethod]
    public async Task GenerateAllForJobAsync_WithoutASignedInUser_GeneratesForTheJobOwner()
    {
        var ownerId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(ownerId, batchJobId, "tshirt");
        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = ownerId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{templateId:D}\"]}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        // The background worker has no request, so there is no signed-in user.
        var result = await CreateService(null, templates: TemplateRepository(MakeTemplate(templateId, "tshirt")), batches: batches,
                rows: MockRows(row), products: MockProducts(product), designImages: MockDesignImages(image),
                mockupImages: mockupImages, compositor: StubCompositor())
            .GenerateAllForJobAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(1);
        added.Should().ContainSingle(m => m.DesignImageId == image.Id && m.MockupTemplateId == templateId);
    }

    [TestMethod]
    [DataRow("draft")]
    [DataRow("queued")]
    [DataRow("running")]
    [DataRow("failed")]
    public async Task GenerateAllForJobAsync_UnlessTheJobFinishedWithImages_ReturnsNotReady(string status)
    {
        var batchJobId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = Guid.NewGuid(), Name = "B", Status = status,
            Config = $"{{\"mockupTemplateIds\":[\"{Guid.NewGuid():D}\"]}}"
        });

        var result = await CreateService(null, batches: batches).GenerateAllForJobAsync(batchJobId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.JobNotReady");
    }

    [TestMethod]
    public async Task GenerateAllForJobAsync_WhenNoTemplatesSelected_ChangesNothing()
    {
        var batchJobId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob { Id = batchJobId, UserId = Guid.NewGuid(), Name = "B", Status = "completed", Config = "{}" });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        var result = await CreateService(null, batches: batches, mockupImages: mockupImages).GenerateAllForJobAsync(batchJobId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.NoTemplatesSelected");
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GenerateAllAsync_WithMixedProductTypes_EachProductGetsItsCompatibleTemplate()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var teeTemplateId = Guid.NewGuid();
        var mugTemplateId = Guid.NewGuid();
        var (rowTee, productTee, imageTee) = MakeProductRow(userId, batchJobId, "tshirt");
        var (rowMug, productMug, imageMug) = MakeProductRow(userId, batchJobId, "mug");

        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{teeTemplateId:D}\",\"{mugTemplateId:D}\"]}}"
        });
        var templates = TemplateRepository(
            MakeTemplate(teeTemplateId, "tshirt"),
            MakeTemplate(mugTemplateId, "mug"));
        var rows = MockRows(rowTee, rowMug);
        var products = MockProducts(productTee, productMug);
        var designImages = MockDesignImages(imageTee, imageMug);
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);
        var compositor = StubCompositor();

        var result = await CreateService(userId, templates: templates, batches: batches, rows: rows,
                products: products, designImages: designImages, mockupImages: mockupImages, compositor: compositor)
            .GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(2);
        added.Should().HaveCount(2);
        added.Should().Contain(m => m.DesignImageId == imageTee.Id && m.MockupTemplateId == teeTemplateId);
        added.Should().Contain(m => m.DesignImageId == imageMug.Id && m.MockupTemplateId == mugTemplateId);
    }

    [TestMethod]
    public async Task GenerateAllAsync_WhenProductMatchesTwoTemplates_GeneratesOneMockupPerTemplate()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var templateAId = Guid.NewGuid();
        var templateBId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");

        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{templateAId:D}\",\"{templateBId:D}\"]}}"
        });
        var templates = TemplateRepository(MakeTemplate(templateAId, "tshirt"), MakeTemplate(templateBId, "tshirt"));
        var rows = MockRows(row);
        var products = MockProducts(product);
        var designImages = MockDesignImages(image);
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);
        var compositor = StubCompositor();

        var result = await CreateService(userId, templates: templates, batches: batches, rows: rows,
                products: products, designImages: designImages, mockupImages: mockupImages, compositor: compositor)
            .GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(2);
        added.Select(m => m.MockupTemplateId).Should().BeEquivalentTo([templateAId, templateBId]);
    }

    [TestMethod]
    public Task GenerateAllAsync_WhenUpToDateMockupExists_SkipsItWithoutDuplicating() =>
        RunExistingMockupCase(existingUrl: "https://cdn/composed.jpg", expectedGenerated: 0);

    [TestMethod]
    public Task GenerateAllAsync_WhenTemplateChangedSinceLastRun_RegeneratesAndReturnsOnlyTheNewImage() =>
        RunExistingMockupCase(existingUrl: "https://cdn/made-before-the-template-was-edited.jpg", expectedGenerated: 1);

    private static async Task RunExistingMockupCase(string existingUrl, int expectedGenerated)
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");

        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{templateId:D}\"]}}"
        });
        var templates = TemplateRepository(MakeTemplate(templateId, "tshirt"));
        var rows = MockRows(row);
        var products = MockProducts(product);
        var designImages = MockDesignImages(image);
        var existing = new MockupImage
        {
            Id = Guid.NewGuid(), DesignImageId = image.Id, ProductId = product.Id, MockupTemplateId = templateId,
            StorageProvider = "cloudinary", StorageKey = "mockups/x/y", MockupImageUrl = existingUrl,
            MockupWidthPx = 2000, MockupHeightPx = 2000, ApprovalStatus = "pending"
        };
        var mockupImages = MockRepo(existing);
        var added = CaptureAdded(mockupImages);
        var compositor = StubCompositor();

        var result = await CreateService(userId, templates: templates, batches: batches, rows: rows,
                products: products, designImages: designImages, mockupImages: mockupImages, compositor: compositor)
            .GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(expectedGenerated);
        added.Should().HaveCount(expectedGenerated);
        result.Value.Images.Should().ContainSingle().Which.MockupImageUrl.Should().Be("https://cdn/composed.jpg");
        if (expectedGenerated == 0) result.Value.Images.Single().Id.Should().Be(existing.Id);
    }

    [TestMethod]
    public async Task GetJobMockupsAsync_ReturnsTheMockupsAlreadyMadeAndMakesNone()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var madeId = Guid.NewGuid();
        var notMadeId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");
        var templates = TemplateRepository(MakeTemplate(madeId, "tshirt"), MakeTemplate(notMadeId, "tshirt"));
        var existing = ExistingMockup(image, product, madeId, "https://cdn/composed.jpg");
        var mockupImages = MockRepo(existing);
        var added = CaptureAdded(mockupImages);
        var unitOfWork = MockUnitOfWork();

        var result = await CreateService(userId, templates: templates,
                batches: MockBatches(CompletedJob(batchJobId, userId, madeId, notMadeId)), rows: MockRows(row),
                products: MockProducts(product), designImages: MockDesignImages(image), mockupImages: mockupImages,
                compositor: StubCompositor(), unitOfWork: unitOfWork)
            .GetJobMockupsAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(0);
        // The second template has no mock-up yet: reading does not make it.
        result.Value.Images.Should().ContainSingle().Which.Id.Should().Be(existing.Id);
        added.Should().BeEmpty();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        templates.Verify(x => x.UpdateAsync(It.IsAny<MockupTemplate>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetJobMockupsAsync_MockupFromBeforeTheTemplateWasEdited_IsLeftOut()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");
        var stale = ExistingMockup(image, product, templateId, "https://cdn/made-before-the-template-was-edited.jpg");

        var result = await CreateService(userId, templates: TemplateRepository(MakeTemplate(templateId, "tshirt")),
                batches: MockBatches(CompletedJob(batchJobId, userId, templateId)), rows: MockRows(row),
                products: MockProducts(product), designImages: MockDesignImages(image), mockupImages: MockRepo(stale),
                compositor: StubCompositor())
            .GetJobMockupsAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Images.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("draft", true, DisplayName = "not started yet")]
    [DataRow("completed", false, DisplayName = "no template selected")]
    public async Task GetJobMockupsAsync_JobThatCannotHaveMockupsYet_HasNoneRatherThanAnError(string status, bool withTemplate)
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var job = CompletedJob(batchJobId, userId, withTemplate ? [Guid.NewGuid()] : []);
        job.Status = status;

        var result = await CreateService(userId, batches: MockBatches(job)).GetJobMockupsAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Images.Should().BeEmpty();
        result.Value.Errors.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetJobMockupsAsync_SomeoneElsesJob_IsNotFound()
    {
        var batchJobId = Guid.NewGuid();
        var job = CompletedJob(batchJobId, Guid.NewGuid(), Guid.NewGuid());

        var result = await CreateService(Guid.NewGuid(), batches: MockBatches(job)).GetJobMockupsAsync(batchJobId);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.NotFound);
    }

    private static BatchJob CompletedJob(Guid id, Guid userId, params Guid[] templateIds) => new()
    {
        Id = id, UserId = userId, Name = "B", Status = "completed",
        Config = $"{{\"mockupTemplateIds\":[{string.Join(",", templateIds.Select(templateId => $"\"{templateId:D}\""))}]}}"
    };

    private static MockupImage ExistingMockup(DesignImage image, Product product, Guid templateId, string url) => new()
    {
        Id = Guid.NewGuid(), DesignImageId = image.Id, ProductId = product.Id, MockupTemplateId = templateId,
        StorageProvider = "cloudinary", StorageKey = "mockups/x/y", MockupImageUrl = url,
        MockupWidthPx = 2000, MockupHeightPx = 2000, ApprovalStatus = "pending"
    };

    // ---- test infrastructure ----

    // ---- Realistic print maps and garment recolor ----

    [TestMethod]
    public async Task CreateAsync_GeneratesPrintMapsAndMarksThemCurrent()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var mapService = MockMapService();

        var result = await CreateService(Guid.NewGuid(), templates: templates,
                images: MockImages(new PublicImageUploadResult("https://cdn/x.jpg", 2000, 2000)), mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Classic Tee", "tshirt", 100, 100, 500, 500, SampleFile()));

        result.IsSuccess.Should().BeTrue();
        result.Value.RealisticPrintReady.Should().BeTrue();
        added.Single().PrintMapsSourceUrl.Should().Be("https://cdn/x.jpg");
        mapService.Verify(x => x.StoreHelpersAsync(It.Is<string>(p => p.StartsWith($"mockup-templates/{added.Single().Id:N}-")), It.IsAny<PreparedBasePhoto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_PhotoInAnotherFormat_IsAnalyzedFromItsStoredJpegAndNeverAsUploaded()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var mapService = MockMapService();

        // A TIFF sent as "image/png": what the file is decides, not what it was labelled.
        var result = await CreateService(Guid.NewGuid(), templates: templates,
                images: MockImages(new PublicImageUploadResult("https://cdn/x.tiff", 2000, 2000)), mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Classic Tee", "tshirt", 100, 100, 500, 500, TiffFile()));

        result.IsSuccess.Should().BeTrue();
        result.Value.RealisticPrintReady.Should().BeTrue();
        added.Single().PrintMapsSourceUrl.Should().Be("https://cdn/x.tiff");
        mapService.Verify(x => x.PrepareAsync(It.Is<byte[]>(bytes => bytes.SequenceEqual(TiffBytes)), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        mapService.Verify(x => x.DownloadAsync("https://cdn/x.tiff", It.IsAny<CancellationToken>()), Times.Once);
        mapService.Verify(x => x.PrepareAsync(It.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 1, 2, 3 })), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_PhotoThatIsReadAsUploaded_IsNotDownloadedAgain()
    {
        var mapService = MockMapService();

        await CreateService(Guid.NewGuid(), templates: TemplateRepository(), mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Classic Tee", "tshirt", 100, 100, 500, 500, SampleFile()));

        mapService.Verify(x => x.PrepareAsync(It.IsAny<byte[]>(), false, It.IsAny<CancellationToken>()), Times.Once);
        mapService.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_NewPhoto_IsStoredBesideTheOldOneSoEarlierMockupsKeepTheirs()
    {
        var userId = Guid.NewGuid();
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.UserId = userId;
        var templates = TemplateRepository(template);
        SetupGetById(templates, template.Id);
        var images = MockImages(new PublicImageUploadResult("https://cdn/second.jpg", 2000, 2000));
        var keys = new List<string>();
        images.Setup(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((UploadFileDto _, string key, CancellationToken _) => keys.Add(key))
            .ReturnsAsync(new PublicImageUploadResult("https://cdn/second.jpg", 2000, 2000));

        var result = await CreateService(userId, templates: templates, images: images)
            .UpdateAsync(template.Id, new UpdateMockupTemplateRequestDto("Tee", "tshirt", 0, 0, 100, 100, SampleFile()));

        result.IsSuccess.Should().BeTrue();
        template.BaseImageUrl.Should().Be("https://cdn/second.jpg");
        keys.Should().ContainSingle().Which.Should().StartWith($"mockup-templates/{template.Id:N}-").And.EndWith("-photo")
            .And.NotBe(MockupRules.BaseImageKey(template.Id), "the first photo is not overwritten");
        images.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_RefusedAfterTheNewPhotoWasStored_RemovesOnlyThatPhoto()
    {
        var userId = Guid.NewGuid();
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.UserId = userId;
        var templates = TemplateRepository(template);
        SetupGetById(templates, template.Id);
        var images = MockImages(new PublicImageUploadResult("https://cdn/second.jpg", 2000, 2000));
        var stored = new List<string>();
        var deleted = new List<string>();
        images.Setup(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((UploadFileDto _, string key, CancellationToken _) => stored.Add(key))
            .ReturnsAsync(new PublicImageUploadResult("https://cdn/second.jpg", 2000, 2000));
        images.Setup(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string key, CancellationToken _) => deleted.Add(key))
            .Returns(Task.CompletedTask);

        // Recoloring is asked for, but the new photo shows a dark garment.
        var result = await CreateService(userId, templates: templates, images: images, mapService: MockMapService(luminance: 0.15))
            .UpdateAsync(template.Id, new UpdateMockupTemplateRequestDto("Tee", "tshirt", 0, 0, 100, 100, SampleFile(), AllowRecolor: true));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BatchMockups.NotRecolorable");
        deleted.Should().Equal(stored);
        deleted.Should().NotContain(MockupRules.BaseImageKey(template.Id));
        templates.Verify(x => x.UpdateAsync(It.IsAny<MockupTemplate>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_NewPhotoInAnotherFormat_IsAnalyzedFromItsStoredJpeg()
    {
        var userId = Guid.NewGuid();
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.UserId = userId;
        var templates = TemplateRepository(template);
        SetupGetById(templates, template.Id);
        var mapService = MockMapService();

        var result = await CreateService(userId, templates: templates,
                images: MockImages(new PublicImageUploadResult("https://cdn/new.tiff", 2000, 2000)), mapService: mapService)
            .UpdateAsync(template.Id, new UpdateMockupTemplateRequestDto("Tee", "tshirt", 0, 0, 100, 100, TiffFile()));

        result.IsSuccess.Should().BeTrue();
        template.PrintMapsSourceUrl.Should().Be("https://cdn/new.tiff");
        mapService.Verify(x => x.PrepareAsync(It.Is<byte[]>(bytes => bytes.SequenceEqual(TiffBytes)), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        mapService.Verify(x => x.DownloadAsync("https://cdn/new.tiff", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PreviewGarmentMaskAsync_PhotoInAnotherFormat_IsNotAnalyzed()
    {
        var mapService = MockMapService();

        var result = await CreateService(Guid.NewGuid(), mapService: mapService).PreviewGarmentMaskAsync(TiffFile());

        result.IsSuccess.Should().BeTrue();
        result.Value.Recolorable.Should().BeFalse();
        result.Value.MaskDataUrl.Should().BeNull();
        mapService.Verify(x => x.PrepareAsync(It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        mapService.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, true, DisplayName = "PNG")]
    [DataRow(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true, DisplayName = "JPEG")]
    [DataRow(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, true, DisplayName = "WebP")]
    [DataRow(new byte[] { 0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0 }, false, DisplayName = "TIFF")]
    [DataRow(new byte[] { 0x49, 0x49, 0x2B, 0x00, 8, 0, 0, 0 }, false, DisplayName = "BigTIFF")]
    [DataRow(new byte[] { 0x42, 0x4D, 0, 0, 0, 0 }, false, DisplayName = "BMP")]
    [DataRow(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, false, DisplayName = "GIF")]
    [DataRow(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, false, DisplayName = "another RIFF file")]
    [DataRow(new byte[] { 0x89, 0x50 }, false, DisplayName = "cut short")]
    [DataRow(new byte[] { }, false, DisplayName = "empty")]
    public void IsDirectlyReadable_GoesByTheFirstBytesOfTheFile(byte[] photo, bool expected) =>
        MockupImageValidators.IsDirectlyReadable(photo).Should().Be(expected);

    [TestMethod]
    public async Task CreateAsync_WhenMapGenerationFails_StillSavesThePlainTemplate()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var mapService = MockMapService();
        mapService.Setup(x => x.PrepareAsync(It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unknown image format"));

        var result = await CreateService(Guid.NewGuid(), templates: templates, mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Classic Tee", "tshirt", 100, 100, 500, 500, SampleFile()));

        result.IsSuccess.Should().BeTrue();
        result.Value.RealisticPrintReady.Should().BeFalse();
        added.Single().PrintMapsSourceUrl.Should().BeNull();
    }

    [TestMethod]
    public async Task CreateAsync_WithRecolorOnADarkGarment_RejectsAndDeletesTheUpload()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var images = MockImages();
        var mapService = MockMapService(luminance: 0.15);

        var result = await CreateService(Guid.NewGuid(), templates: templates, images: images, mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Black Tee", "tshirt", 100, 100, 500, 500, SampleFile(), AllowRecolor: true));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BatchMockups.NotRecolorable");
        added.Should().BeEmpty();
        images.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_WithRecolorOnALightGarment_AllowsRecolorAndExposesTheMask()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var mapService = MockMapService();
        var compositor = StubCompositor();
        compositor.Setup(x => x.BuildAssetUrl(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((_, key) => $"https://cdn/{key}.png");

        var result = await CreateService(Guid.NewGuid(), templates: templates, compositor: compositor, mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("White Tee", "tshirt", 100, 100, 500, 500, SampleFile(), AllowRecolor: true));

        result.IsSuccess.Should().BeTrue();
        added.Single().AllowRecolor.Should().BeTrue();
        result.Value.GarmentMaskUrl.Should().Be($"https://cdn/mockup-templates/{added.Single().Id:N}-{added.Single().PrintMapsVersion}-mask.png");
    }

    [TestMethod]
    public async Task CreateAsync_WithRecolorAndAColor_StoresTheTemplateColor()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var mapService = MockMapService();

        var result = await CreateService(Guid.NewGuid(), templates: templates, compositor: StubCompositor(), mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Navy Tee", "tshirt", 100, 100, 500, 500, SampleFile(), AllowRecolor: true, GarmentColor: "#1f2a44"));

        result.IsSuccess.Should().BeTrue();
        result.Value.GarmentColor.Should().Be("#1F2A44");
        added.Single().GarmentColor.Should().Be("#1F2A44");
    }

    [TestMethod]
    public async Task CreateAsync_WithAColorButNoRecolor_ReturnsConflict()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);

        var result = await CreateService(Guid.NewGuid(), templates: templates)
            .CreateAsync(new CreateMockupTemplateRequestDto("Navy Tee", "tshirt", 100, 100, 500, 500, SampleFile(), GarmentColor: "#1F2A44"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BatchMockups.RecolorNotAllowed");
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateAsync_TurningRecolorOff_ClearsTheTemplateColor()
    {
        var userId = Guid.NewGuid();
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.UserId = userId;
        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = 7;
        template.AllowRecolor = true;
        template.GarmentColor = "#1F2A44";
        var templates = TemplateRepository(template);
        SetupGetById(templates, template.Id);

        var result = await CreateService(userId, templates: templates)
            .UpdateAsync(template.Id, new UpdateMockupTemplateRequestDto("Tee", "tshirt", 0, 0, 100, 100, null));

        result.IsSuccess.Should().BeTrue();
        template.AllowRecolor.Should().BeFalse();
        template.GarmentColor.Should().BeNull();
    }

    [TestMethod]
    public async Task GenerateAllAsync_WithoutBatchColors_UsesTheTemplatesOwnColor()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");
        var template = MakeTemplate(templateId, "tshirt");
        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = 7;
        template.AllowRecolor = true;
        template.GarmentColor = "#1F2A44";

        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{templateId:D}\"]}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        var result = await CreateService(userId, templates: TemplateRepository(template), batches: batches, rows: MockRows(row),
                products: MockProducts(product), designImages: MockDesignImages(image), mockupImages: mockupImages, compositor: StubCompositor())
            .GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        added.Should().ContainSingle().Which.GarmentColor.Should().Be("#1F2A44");
    }

    [TestMethod]
    public async Task GenerateAllAsync_MarksEachMockupAsCompositedFromItsDesign()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");
        var template = MakeTemplate(templateId, "tshirt");
        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = 7;
        template.AllowRecolor = true;
        template.GarmentColor = "#1F2A44";

        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{templateId:D}\"]}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        await CreateService(userId, templates: TemplateRepository(template), batches: batches, rows: MockRows(row),
                products: MockProducts(product), designImages: MockDesignImages(image), mockupImages: mockupImages, compositor: StubCompositor())
            .GenerateAllAsync(batchJobId);

        // What a video source needs to know: where the mock-up came from, which design it shows and which color variant it is.
        var mockup = added.Should().ContainSingle().Subject;
        mockup.SourceType.Should().Be("generated");
        mockup.ArtworkGroupKey.Should().Be(image.Id.ToString());
        mockup.VariantKey.Should().Be("#1F2A44");
        mockup.Role.Should().Be("Hero");
        mockup.Regions.Should().Be("{}");
        mockup.MetadataRevision.Should().Be(1);
        mockup.ContentHash.Should().BeNull();
    }

    [TestMethod]
    public async Task UpdateAsync_WhenMapsAreMissing_BackfillsThemWithoutANewPhoto()
    {
        var userId = Guid.NewGuid();
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.UserId = userId;
        var templates = TemplateRepository(template);
        SetupGetById(templates, template.Id);
        var mapService = MockMapService();

        var result = await CreateService(userId, templates: templates, mapService: mapService)
            .UpdateAsync(template.Id, new UpdateMockupTemplateRequestDto("Tee", "tshirt", 0, 0, 100, 100, null));

        result.IsSuccess.Should().BeTrue();
        template.PrintMapsSourceUrl.Should().Be(template.BaseImageUrl);
        mapService.Verify(x => x.StoreHelpersAsync(It.Is<string>(p => p.StartsWith($"mockup-templates/{template.Id:N}-")), It.IsAny<PreparedBasePhoto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAsync_NewPhotoSmallerThanThePrintArea_IsRejectedBeforeAnythingIsStored()
    {
        var userId = Guid.NewGuid();
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.UserId = userId;
        var templates = TemplateRepository(template);
        SetupGetById(templates, template.Id);
        var images = MockImages();
        var mapService = MockMapService(width: 700, height: 700);

        var result = await CreateService(userId, templates: templates, images: images, mapService: mapService)
            .UpdateAsync(template.Id, new UpdateMockupTemplateRequestDto("Tee", "tshirt", 1418, 1475, 1152, 1039, SampleFile()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
        template.BaseImageUrl.Should().Be("https://cdn/base.jpg");
        images.Verify(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mapService.Verify(x => x.StoreHelpersAsync(It.IsAny<string>(), It.IsAny<PreparedBasePhoto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_PhotoAlreadyAnalyzed_IsNotAnalyzedAgainAndOnlyTheRecolorChoiceChanges()
    {
        var userId = Guid.NewGuid();
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.UserId = userId;
        template.GarmentIsLight = true;
        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = 7;
        var templates = TemplateRepository(template);
        SetupGetById(templates, template.Id);
        var images = MockImages();
        var mapService = MockMapService();

        var result = await CreateService(userId, templates: templates, images: images, mapService: mapService)
            .UpdateAsync(template.Id, new UpdateMockupTemplateRequestDto("Tee", "tshirt", 0, 0, 100, 100, null, AllowRecolor: true));

        result.IsSuccess.Should().BeTrue();
        template.AllowRecolor.Should().BeTrue();
        template.PrintMapsVersion.Should().Be(7);
        mapService.Verify(x => x.PrepareAsync(It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        images.Verify(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_WithCurrentMaps_PassesMapKeysAndDesignSize()
    {
        var (service, compositor, templateId, designImageId) = CompositeCase(mapsCurrent: true, allowRecolor: false);

        var result = await service.GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
        compositor.Verify(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(),
            It.Is<MockupLayers?>(l => l!.DesignWidthPx == 1024 && l.DisplacementMapKey == $"mockup-templates/{templateId:N}-7-displace"
                && l.GarmentMaskKey == $"mockup-templates/{templateId:N}-7-mask"
                && l.GarmentColor == null && l.MultiplyDesign && l.BasePhotoShortSidePx == 2000)), Times.Once);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_WithAColor_RecolorsInsteadOfMultiplyingTheDesign()
    {
        var (service, compositor, templateId, designImageId) = CompositeCase(mapsCurrent: true, allowRecolor: true);

        await service.GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null, "#1F2A44"));

        compositor.Verify(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(),
            It.Is<MockupLayers?>(l => l!.GarmentColor == "#1F2A44" && !l.MultiplyDesign)), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_OnADarkGarment_DoesNotMultiplyDesigns()
    {
        var templates = TemplateRepository();
        var added = CaptureAdded(templates);
        var mapService = MockMapService(luminance: 0.15);

        var result = await CreateService(Guid.NewGuid(), templates: templates, mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Black Tee", "tshirt", 100, 100, 500, 500, SampleFile()));

        result.IsSuccess.Should().BeTrue();
        added.Single().GarmentIsLight.Should().BeFalse();
        added.Single().PrintMapsVersion.Should().NotBeNull();
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_WithStaleMaps_FallsBackToNoMaps()
    {
        var (service, compositor, templateId, designImageId) = CompositeCase(mapsCurrent: false, allowRecolor: false);

        await service.GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        compositor.Verify(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(),
            It.Is<MockupLayers?>(l => l!.DisplacementMapKey == null && l.GarmentMaskKey == null && !l.MultiplyDesign)), Times.Once);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_ColorOnATemplateWithoutRecolor_ReturnsConflict()
    {
        var (service, _, templateId, designImageId) = CompositeCase(mapsCurrent: true, allowRecolor: false);

        var result = await service.GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null, "#1F2A44"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BatchMockups.RecolorNotAllowed");
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_ColorOnARecolorableTemplate_StoresTheColor()
    {
        var (service, compositor, templateId, designImageId) = CompositeCase(mapsCurrent: true, allowRecolor: true);

        var result = await service.GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null, "#1f2a44"));

        result.IsSuccess.Should().BeTrue();
        result.Value.GarmentColor.Should().Be("#1F2A44");
        compositor.Verify(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(),
            It.Is<MockupLayers?>(l => l!.GarmentMaskKey == $"mockup-templates/{templateId:N}-7-mask" && l.GarmentColor == "#1F2A44")), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAllAsync_WithColors_MakesOneMockupPerColorOnlyForRecolorableTemplates()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var recolorId = Guid.NewGuid();
        var plainId = Guid.NewGuid();
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");
        var recolorable = MakeTemplate(recolorId, "tshirt");
        recolorable.PrintMapsSourceUrl = recolorable.BaseImageUrl;
        recolorable.PrintMapsVersion = 7;
        recolorable.AllowRecolor = true;

        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{recolorId:D}\",\"{plainId:D}\"],\"mockupGarmentColors\":[\"#1F2A44\",\"#B22222\"]}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);
        var compositor = new Mock<IMockupCompositor>();
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>()))
            .Returns<string, string, MockupPosition, MockupLayers?>((_, _, _, layers) => $"https://cdn/{Guid.NewGuid():N}?c={layers?.GarmentColor}");

        var result = await CreateService(userId, templates: TemplateRepository(recolorable, MakeTemplate(plainId, "tshirt")),
                batches: batches, rows: MockRows(row), products: MockProducts(product), designImages: MockDesignImages(image),
                mockupImages: mockupImages, compositor: compositor)
            .GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(3);
        added.Where(m => m.MockupTemplateId == recolorId).Select(m => m.GarmentColor).Should().BeEquivalentTo(["#1F2A44", "#B22222"]);
        added.Where(m => m.MockupTemplateId == plainId).Should().ContainSingle().Which.GarmentColor.Should().BeNull();
    }

    [TestMethod]
    public async Task ApplyAsync_StoresGarmentColorsUppercased()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var batch = new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "completed", Config = "{}" };
        var batches = MockBatches(batch);
        batches.Setup(r => r.UpdateAsync(It.IsAny<BatchJob>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await CreateService(userId, templates: TemplateRepository(MakeTemplate(templateId, "tshirt")), batches: batches, rows: MockRows())
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([templateId], ["#1f2a44"]));

        result.IsSuccess.Should().BeTrue();
        result.Value.GarmentColors.Should().Equal("#1F2A44");
        batch.Config.Should().Contain("\"mockupGarmentColors\":[\"#1F2A44\"]");
    }

    [TestMethod]
    public async Task ApplyAsync_StoresColorsPerTemplate_AndReturnsThemUppercased()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var teeId = Guid.NewGuid();
        var hoodieId = Guid.NewGuid();
        var batch = new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "completed", Config = "{}" };
        var batches = MockBatches(batch);
        batches.Setup(r => r.UpdateAsync(It.IsAny<BatchJob>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var colors = new Dictionary<Guid, IReadOnlyList<string>>
        {
            [teeId] = ["#1f2a44", "#b22222"],
            [hoodieId] = ["#2f4f3a"],
        };

        var result = await CreateService(userId, templates: TemplateRepository(MakeTemplate(teeId, "tshirt"), MakeTemplate(hoodieId, "hoodie")),
                batches: batches, rows: MockRows())
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([teeId, hoodieId], TemplateColors: colors));

        result.IsSuccess.Should().BeTrue();
        result.Value.TemplateColors![teeId].Should().Equal("#1F2A44", "#B22222");
        result.Value.TemplateColors[hoodieId].Should().Equal("#2F4F3A");
        batch.Config.Should().Contain("mockupTemplateColors").And.Contain("#1F2A44");

        // What was stored is what a later read returns.
        var read = await CreateService(userId, batches: batches).GetSelectionAsync(batchId);
        read.Value.TemplateColors![teeId].Should().Equal("#1F2A44", "#B22222");
    }

    [TestMethod]
    public async Task ApplyAsync_WithoutPerTemplateColors_RemovesTheOnesStoredBefore()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var batch = new BatchJob
        {
            Id = batchId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateColors\":{{\"{templateId:D}\":[\"#1F2A44\"]}}}}"
        };
        var batches = MockBatches(batch);
        batches.Setup(r => r.UpdateAsync(It.IsAny<BatchJob>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await CreateService(userId, templates: TemplateRepository(MakeTemplate(templateId, "tshirt")), batches: batches, rows: MockRows())
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([templateId]));

        result.IsSuccess.Should().BeTrue();
        result.Value.TemplateColors.Should().BeEmpty();
        batch.Config.Should().NotContain("mockupTemplateColors");
    }

    [TestMethod]
    public async Task ApplyAsync_WithColorsForATemplateThatIsNotSelected_ReturnsValidationError()
    {
        var selected = Guid.NewGuid();
        var other = Guid.NewGuid();
        var colors = new Dictionary<Guid, IReadOnlyList<string>> { [other] = ["#1F2A44"] };

        var result = await CreateService(Guid.NewGuid())
            .ApplyAsync(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto([selected], TemplateColors: colors));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
    }

    [TestMethod]
    public async Task ApplyAsync_WithTooManyOrRepeatedColorsForATemplate_ReturnsValidationError()
    {
        var templateId = Guid.NewGuid();
        var tooMany = new Dictionary<Guid, IReadOnlyList<string>>
        {
            [templateId] = ["#111111", "#222222", "#333333", "#444444", "#555555", "#666666"],
        };
        var repeated = new Dictionary<Guid, IReadOnlyList<string>> { [templateId] = ["#111111", "#111111"] };
        var invalid = new Dictionary<Guid, IReadOnlyList<string>> { [templateId] = ["navy"] };
        var service = CreateService(Guid.NewGuid());

        foreach (var colors in new[] { tooMany, repeated, invalid })
        {
            var result = await service.ApplyAsync(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto([templateId], TemplateColors: colors));

            result.IsFailure.Should().BeTrue();
            result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
        }
    }

    // ---- Approval: which design images get mock-ups ----

    private static DesignImage AnotherVariation(DesignImage image, int variation, string approval) => new()
    {
        Id = Guid.NewGuid(), ProductId = image.ProductId, AiPromptId = image.AiPromptId, BatchJobProductId = image.BatchJobProductId,
        StorageKey = $"{image.StorageKey}/{variation}", ImageGeneratorModel = "m", StorageProvider = "cloudinary",
        ImageUrl = $"https://cdn/design-{variation}.png", ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png",
        FileSizeMb = 1, ApprovalStatus = approval, VariationIndex = variation, GenerationTimeSeconds = 1
    };

    private static async Task<(APCS.Common.Models.Result<APCS.Application.Features.BatchMockups.Dtos.Response.GenerateAllMockupsResultDto> Result, List<MockupImage> Added)> GenerateWithApprovalAsync(
        string? requireApproval, params (int Variation, string Approval)[] images)
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var (row, product, first) = MakeProductRow(userId, batchJobId, "tshirt");
        var designs = images.Select(entry => AnotherVariation(first, entry.Variation, entry.Approval)).ToArray();
        var approvalKey = requireApproval is null ? string.Empty : $",\"requireApproval\":{requireApproval}";
        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{templateId:D}\"]{approvalKey}}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        var result = await CreateService(userId, templates: TemplateRepository(MakeTemplate(templateId, "tshirt")), batches: batches, rows: MockRows(row),
                products: MockProducts(product), designImages: MockDesignImages(designs), mockupImages: mockupImages, compositor: ColorAwareCompositor())
            .GenerateAllAsync(batchJobId);
        return (result, added);
    }

    [TestMethod]
    public async Task GenerateAllAsync_WhenApprovalIsRequired_MakesMockupsOnlyForApprovedImages()
    {
        var (result, added) = await GenerateWithApprovalAsync("true", (1, "approved"), (2, "pending"), (3, "rejected"));

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(1);
        added.Should().ContainSingle();
        result.Value.NoApprovedImageCount.Should().Be(0);
    }

    [TestMethod]
    public async Task GenerateAllAsync_WhenSeveralImagesAreApproved_EachOneGetsItsMockups()
    {
        var (result, added) = await GenerateWithApprovalAsync("true", (1, "approved"), (2, "approved"), (3, "rejected"));

        result.Value.GeneratedCount.Should().Be(2);
        added.Select(m => m.DesignImageId).Distinct().Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GenerateAllAsync_WhenNoImageOfAProductIsApproved_CountsItAndMakesNothing()
    {
        var (result, added) = await GenerateWithApprovalAsync("true", (1, "pending"), (2, "rejected"));

        result.IsSuccess.Should().BeTrue();
        added.Should().BeEmpty();
        result.Value.NoApprovedImageCount.Should().Be(1);
        result.Value.NoDesignImageCount.Should().Be(0);
    }

    [TestMethod]
    public async Task GenerateAllAsync_WhenApprovalIsAutomatic_MakesMockupsForEveryApprovedImage()
    {
        var (result, added) = await GenerateWithApprovalAsync("false", (1, "approved"), (2, "approved"));

        result.Value.GeneratedCount.Should().Be(2);
        added.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GenerateAllAsync_ForAJobFromBeforeApprovalExisted_StillUsesTheFirstVariationOnly()
    {
        var (result, added) = await GenerateWithApprovalAsync(null, (2, "pending"), (1, "pending"));

        result.Value.GeneratedCount.Should().Be(1);
        added.Should().ContainSingle().Which.DesignImageId.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task GenerateAllForJobAsync_WhenApprovalIsRequired_LeavesTheMockupsToTheSeller()
    {
        var batchJobId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = Guid.NewGuid(), Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{Guid.NewGuid():D}\"],\"requireApproval\":true}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        var result = await CreateService(null, batches: batches, mockupImages: mockupImages).GenerateAllForJobAsync(batchJobId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("BatchMockups.ApprovalPending");
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GenerateAllAsync_WithPerTemplateColors_EachTemplateIsMadeOnlyInItsOwnColors()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var (teeAId, teeBId) = (Guid.NewGuid(), Guid.NewGuid());
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");
        var teeA = RecolorableTemplate(teeAId);
        var teeB = RecolorableTemplate(teeBId);
        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{teeAId:D}\",\"{teeBId:D}\"],\"mockupTemplateColors\":{{\"{teeAId:D}\":[\"#1F2A44\"],\"{teeBId:D}\":[\"#B22222\",\"#2F4F3A\"]}}}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        var result = await CreateService(userId, templates: TemplateRepository(teeA, teeB), batches: batches, rows: MockRows(row),
                products: MockProducts(product), designImages: MockDesignImages(image), mockupImages: mockupImages, compositor: ColorAwareCompositor())
            .GenerateAllAsync(batchJobId);

        result.IsSuccess.Should().BeTrue();
        added.Where(m => m.MockupTemplateId == teeAId).Select(m => m.GarmentColor).Should().BeEquivalentTo(["#1F2A44"]);
        added.Where(m => m.MockupTemplateId == teeBId).Select(m => m.GarmentColor).Should().BeEquivalentTo(["#B22222", "#2F4F3A"]);
    }

    [TestMethod]
    public async Task GenerateAllAsync_TemplateWithoutItsOwnColors_UsesTheSharedColorList()
    {
        var userId = Guid.NewGuid();
        var batchJobId = Guid.NewGuid();
        var (teeAId, teeBId) = (Guid.NewGuid(), Guid.NewGuid());
        var (row, product, image) = MakeProductRow(userId, batchJobId, "tshirt");
        var batches = MockBatches(new BatchJob
        {
            Id = batchJobId, UserId = userId, Name = "B", Status = "completed",
            Config = $"{{\"mockupTemplateIds\":[\"{teeAId:D}\",\"{teeBId:D}\"],\"mockupGarmentColors\":[\"#2B4FA3\"],\"mockupTemplateColors\":{{\"{teeAId:D}\":[\"#1F2A44\"]}}}}"
        });
        var mockupImages = MockRepo<MockupImage>();
        var added = CaptureAdded(mockupImages);

        await CreateService(userId, templates: TemplateRepository(RecolorableTemplate(teeAId), RecolorableTemplate(teeBId)), batches: batches,
                rows: MockRows(row), products: MockProducts(product), designImages: MockDesignImages(image), mockupImages: mockupImages,
                compositor: ColorAwareCompositor())
            .GenerateAllAsync(batchJobId);

        added.Single(m => m.MockupTemplateId == teeAId).GarmentColor.Should().Be("#1F2A44");
        added.Single(m => m.MockupTemplateId == teeBId).GarmentColor.Should().Be("#2B4FA3");
    }

    private static MockupTemplate RecolorableTemplate(Guid id)
    {
        var template = MakeTemplate(id, "tshirt");
        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = 7;
        template.AllowRecolor = true;
        return template;
    }

    private static Mock<IMockupCompositor> ColorAwareCompositor()
    {
        var compositor = new Mock<IMockupCompositor>();
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>()))
            .Returns<string, string, MockupPosition, MockupLayers?>((_, _, _, layers) => $"https://cdn/{Guid.NewGuid():N}?c={layers?.GarmentColor}");
        return compositor;
    }

    [TestMethod]
    public async Task ApplyAsync_WithAnInvalidColor_ReturnsValidationError()
    {
        var result = await CreateService(Guid.NewGuid())
            .ApplyAsync(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto([Guid.NewGuid()], ["navy"]));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
    }

    private static (MockupTemplateService Service, Mock<IMockupCompositor> Compositor, Guid TemplateId, Guid DesignImageId) CompositeCase(bool mapsCurrent, bool allowRecolor)
    {
        var userId = Guid.NewGuid();
        var (_, product, image) = MakeProductRow(userId, Guid.NewGuid(), "tshirt");
        var template = MakeTemplate(Guid.NewGuid(), "tshirt");
        template.PrintMapsSourceUrl = mapsCurrent ? template.BaseImageUrl : "https://cdn/an-older-photo.jpg";
        template.PrintMapsVersion = 7;
        template.AllowRecolor = allowRecolor;
        template.GarmentIsLight = true;
        var mockupImages = MockRepo<MockupImage>();
        CaptureAdded(mockupImages);
        var compositor = StubCompositor();

        var service = CreateService(userId, templates: TemplateRepository(template), products: MockProducts(product),
            designImages: MockDesignImages(image), mockupImages: mockupImages, compositor: compositor);
        return (service, compositor, template.Id, image.Id);
    }

    [TestMethod]
    public async Task PreviewGarmentMaskAsync_LightGarment_ReturnsTheMaskAsADataUrl()
    {
        var result = await CreateService(Guid.NewGuid(), mapService: MockMapService()).PreviewGarmentMaskAsync(SampleFile());

        result.IsSuccess.Should().BeTrue();
        result.Value.Recolorable.Should().BeTrue();
        result.Value.Reason.Should().BeNull();
        result.Value.MaskDataUrl.Should().Be("data:image/png;base64,CQgH");
    }

    [TestMethod]
    public async Task PreviewGarmentMaskAsync_DarkGarment_SaysWhyItCannotBeRecolored()
    {
        var result = await CreateService(Guid.NewGuid(), mapService: MockMapService(luminance: 0.2)).PreviewGarmentMaskAsync(SampleFile());

        result.IsSuccess.Should().BeTrue();
        result.Value.Recolorable.Should().BeFalse();
        result.Value.Reason.Should().Contain("too dark");
    }

    [TestMethod]
    public async Task PreviewGarmentMaskAsync_UnreadablePhoto_IsNotRecolorableRatherThanAnError()
    {
        var mapService = MockMapService();
        mapService.Setup(x => x.PrepareAsync(It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unknown format"));

        var result = await CreateService(Guid.NewGuid(), mapService: mapService).PreviewGarmentMaskAsync(SampleFile());

        result.IsSuccess.Should().BeTrue();
        result.Value.Recolorable.Should().BeFalse();
        result.Value.MaskDataUrl.Should().BeNull();
    }

    [TestMethod]
    public async Task CreateAsync_ModelNotInstalled_IsRejectedBeforeAnythingIsStored()
    {
        var images = MockImages();
        var templates = TemplateRepository();
        var mapService = MockMapService();
        mapService.SetupGet(x => x.IsAvailable).Returns(false);

        var result = await CreateService(Guid.NewGuid(), templates: templates, images: images, mapService: mapService)
            .CreateAsync(new CreateMockupTemplateRequestDto("Tee", "tshirt", 10, 10, 100, 100, SampleFile()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BatchMockups.ProcessingUnavailable");
        images.Invocations.Should().BeEmpty();
        mapService.Verify(x => x.PrepareAsync(It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PreviewGarmentMaskAsync_ModelNotInstalled_SaysSo()
    {
        var mapService = MockMapService();
        mapService.SetupGet(x => x.IsAvailable).Returns(false);

        var result = await CreateService(Guid.NewGuid(), mapService: mapService).PreviewGarmentMaskAsync(SampleFile());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BatchMockups.ProcessingUnavailable");
    }

    [TestMethod]
    public async Task PreviewGarmentMaskAsync_NotAnImage_ReturnsValidationError()
    {
        var file = new UploadFileDto("notes.txt", "text/plain", 4, new MemoryStream([1, 2, 3, 4]));

        var result = await CreateService(Guid.NewGuid()).PreviewGarmentMaskAsync(file);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
    }

    private static Mock<IMockupMapService> MockMapService(double coverage = 0.35, double luminance = 0.93, int width = 0, int height = 0)
    {
        var mapService = new Mock<IMockupMapService>();
        mapService.SetupGet(x => x.IsAvailable).Returns(true);
        mapService.Setup(x => x.PrepareAsync(It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] _, bool preview, CancellationToken _) => new PreparedBasePhoto(
                [9, 8, 7], preview ? null : [1], new GarmentMaskStats(coverage, luminance), width, height));
        mapService.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        mapService.Setup(x => x.StoreHelpersAsync(It.IsAny<string>(), It.IsAny<PreparedBasePhoto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mapService;
    }

    private static UploadFileDto SampleFile() => new("photo.jpg", "image/jpeg", 4, new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0]));

    private static readonly byte[] TiffBytes = [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0];

    // A TIFF by its first bytes, labelled as a PNG the way a crafted upload would be.
    private static UploadFileDto TiffFile() => new("photo.png", "image/png", TiffBytes.Length, new MemoryStream(TiffBytes));

    private static (BatchJobProduct Row, Product Product, DesignImage Image) MakeProductRow(Guid userId, Guid batchJobId, string productType)
    {
        var productId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var row = new BatchJobProduct { Id = rowId, BatchJobId = batchJobId, ProductId = productId, SequenceOrder = 1, Status = "completed" };
        var product = new Product { Id = productId, UserId = userId, Name = $"P-{productType}", ProductType = productType, InputDescription = "d", ProcessingStatus = "completed" };
        var image = new DesignImage
        {
            Id = Guid.NewGuid(), ProductId = productId, AiPromptId = Guid.NewGuid(), BatchJobProductId = rowId,
            StorageKey = $"design-images/{productId:N}/0", ImageGeneratorModel = "m", StorageProvider = "cloudinary",
            ImageUrl = "https://cdn/design.png", ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png",
            FileSizeMb = 1, ApprovalStatus = "pending", VariationIndex = 0, GenerationTimeSeconds = 1
        };
        return (row, product, image);
    }

    private static MockupTemplate MakeTemplate(Guid id, string productType) => new()
    {
        Id = id, Name = $"Template-{id:N}", ProductType = productType, BaseImageUrl = "https://cdn/base.jpg",
        PrintAreaConfig = "{\"x\":0,\"y\":0,\"width\":100,\"height\":100}", OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true
    };

    private static Mock<IMockupCompositor> StubCompositor()
    {
        var compositor = new Mock<IMockupCompositor>();
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>()))
            .Returns("https://cdn/composed.jpg");
        return compositor;
    }

    private static MockupTemplateService CreateService(
        Guid? userId,
        Mock<IRepository<MockupTemplate>>? templates = null,
        Mock<IRepository<BatchJob>>? batches = null,
        Mock<IRepository<BatchJobProduct>>? rows = null,
        Mock<IRepository<Product>>? products = null,
        Mock<IRepository<DesignImage>>? designImages = null,
        Mock<IRepository<MockupImage>>? mockupImages = null,
        Mock<IPublicImageService>? images = null,
        Mock<IMockupCompositor>? compositor = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IMockupMapService>? mapService = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(userId.HasValue);
        currentUser.SetupGet(user => user.UserId).Returns(userId);

        return new MockupTemplateService(
            currentUser.Object,
            (templates ?? new Mock<IRepository<MockupTemplate>>()).Object,
            (batches ?? new Mock<IRepository<BatchJob>>()).Object,
            (rows ?? new Mock<IRepository<BatchJobProduct>>()).Object,
            (products ?? new Mock<IRepository<Product>>()).Object,
            (designImages ?? new Mock<IRepository<DesignImage>>()).Object,
            (mockupImages ?? new Mock<IRepository<MockupImage>>()).Object,
            (images ?? MockImages()).Object,
            (compositor ?? new Mock<IMockupCompositor>()).Object,
            (mapService ?? MockMapService()).Object,
            (unitOfWork ?? MockUnitOfWork()).Object,
            new ApplyMockupTemplatesValidator(),
            new CreateMockupTemplateValidator(),
            new UpdateMockupTemplateValidator(),
            new GenerateMockupImageValidator(),
            TimeProvider.System,
            NullLogger<MockupTemplateService>.Instance);
    }

    private static Mock<IRepository<MockupTemplate>> TemplateRepository(params MockupTemplate[] items)
    {
        var repository = new Mock<IRepository<MockupTemplate>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        repository.Setup(r => r.UpdateAsync(It.IsAny<MockupTemplate>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static void SetupGetById(Mock<IRepository<MockupTemplate>> repository, Guid id, MockupTemplate? item = null)
    {
        var found = item ?? repository.Object.Query().SingleOrDefault(x => x.Id == id);
        repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(found);
    }

    private static Mock<IRepository<BatchJob>> MockBatches(params BatchJob[] items)
    {
        var repository = new Mock<IRepository<BatchJob>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        return repository;
    }

    private static Mock<IRepository<BatchJobProduct>> MockRows(params BatchJobProduct[] items)
    {
        var repository = new Mock<IRepository<BatchJobProduct>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        return repository;
    }

    private static Mock<IRepository<Product>> MockProducts(params Product[] items)
    {
        var repository = new Mock<IRepository<Product>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        return repository;
    }

    private static Mock<IRepository<DesignImage>> MockDesignImages(params DesignImage[] items)
    {
        var repository = new Mock<IRepository<DesignImage>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        return repository;
    }

    private static Mock<IRepository<TEntity>> MockRepo<TEntity>(params TEntity[] items) where TEntity : class
    {
        var repository = new Mock<IRepository<TEntity>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        return repository;
    }

    private static List<TEntity> CaptureAdded<TEntity>(Mock<IRepository<TEntity>> repository) where TEntity : class
    {
        var added = new List<TEntity>();
        repository.Setup(r => r.AddAsync(It.IsAny<TEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<TEntity, bool, CancellationToken>((entity, _, _) => added.Add(entity))
            .Returns(Task.CompletedTask);
        return added;
    }

    private static Mock<IPublicImageService> MockImages(PublicImageUploadResult? uploadResult = null)
    {
        var images = new Mock<IPublicImageService>();
        images.Setup(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(uploadResult ?? new PublicImageUploadResult("https://cdn/default.jpg", 2000, 2000));
        images.Setup(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return images;
    }

    private static Mock<IUnitOfWork> MockUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        return unitOfWork;
    }
}
