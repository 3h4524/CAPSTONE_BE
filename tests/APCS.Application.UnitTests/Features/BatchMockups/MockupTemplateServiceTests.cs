using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
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
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>()))
            .Returns<string, string, MockupPosition>((baseUrl, overlay, position) => $"{baseUrl}?l={overlay}&x={position.X}");

        var result = await CreateService(userId, templates: templates, products: products, designImages: designImages,
                mockupImages: mockupImages, compositor: compositor)
            .GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.MockupImageUrl.Should().Be("https://cdn/base.jpg?l=design-images/p/0&x=820");
        captured.Should().ContainSingle();
        compositor.Verify(x => x.BuildCompositeUrl("https://cdn/base.jpg", "design-images/p/0", new MockupPosition(820, 640, 900, 1100)), Times.Once);
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
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>())).Returns("https://cdn/composed.jpg");

        var result = await CreateService(userId, templates: templates, products: products, designImages: designImages,
                mockupImages: mockupImages, compositor: compositor)
            .GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, 50, 60, 300, 400));

        result.IsSuccess.Should().BeTrue();
        compositor.Verify(x => x.BuildCompositeUrl("https://cdn/base.jpg", "design-images/p/0", new MockupPosition(50, 60, 300, 400)), Times.Once);
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
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>()))
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

    // ---- test infrastructure ----

    private static UploadFileDto SampleFile() => new("photo.jpg", "image/jpeg", 4, new MemoryStream([1, 2, 3, 4]));

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
        compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>()))
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
        Mock<IUnitOfWork>? unitOfWork = null)
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
            (unitOfWork ?? MockUnitOfWork()).Object,
            new ApplyMockupTemplatesValidator(),
            new CreateMockupTemplateValidator(),
            new UpdateMockupTemplateValidator(),
            new GenerateMockupImageValidator(),
            TimeProvider.System);
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
