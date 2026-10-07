using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Validators;
using APCS.Domain.Entities;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

/// <summary>
/// Runs the real <see cref="MockupTemplateService"/> plus the real <see cref="CloudinaryMockupCompositor"/>
/// against real repositories on an EF in-memory database. Mock-based tests miss EF change-tracking
/// mistakes (see <c>DesignGenerationProcessingTests</c>), so this exercises the actual persistence path
/// for the "generate composite" use case.
/// </summary>
[TestClass]
public sealed class MockupTemplateProcessingTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GenerateCompositeAsync_WhenValid_PersistsMockupImageAndIncrementsUsageCount()
    {
        await using var db = CreateContext();
        var (templateId, designImageId) = Seed(db);

        var result = await CreateService(db).GenerateCompositeAsync(
            designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.MockupImageUrl.Should().Contain("l_apcs:design-images:prompt-id:0/c_scale,w_900,h_900/fl_layer_apply,g_north_west,x_820,y_740/", "a square design is centered in the 900x1100 print area");

        db.ChangeTracker.Clear();
        var saved = await db.MockupImages.SingleAsync();
        saved.DesignImageId.Should().Be(designImageId);
        saved.MockupTemplateId.Should().Be(templateId);
        saved.ApprovalStatus.Should().Be("pending");
        (await db.MockupTemplates.SingleAsync(t => t.Id == templateId)).UsageCount.Should().Be(1);
    }

    [TestMethod]
    public async Task GenerateCompositeAsync_CalledTwice_IncrementsUsageCountEachTime()
    {
        await using var db = CreateContext();
        var (templateId, designImageId) = Seed(db);
        var service = CreateService(db);

        await service.GenerateCompositeAsync(designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));
        db.ChangeTracker.Clear();
        var result = await CreateService(db).GenerateCompositeAsync(
            designImageId, new GenerateMockupImageRequestDto(templateId, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        (await db.MockupImages.CountAsync()).Should().Be(2);
        (await db.MockupTemplates.SingleAsync(t => t.Id == templateId)).UsageCount.Should().Be(2);
    }

    [TestMethod]
    public async Task GenerateAllAsync_MixedProductTypeBatch_CreatesOneMockupPerProduct_AndIsIdempotentOnRerun()
    {
        await using var db = CreateContext();
        var seed = SeedBatchJob(db);

        var first = await CreateService(db).GenerateAllAsync(seed.BatchJobId);

        first.IsSuccess.Should().BeTrue();
        first.Value.GeneratedCount.Should().Be(2);
        first.Value.Images.Should().HaveCount(2);
        db.ChangeTracker.Clear();
        (await db.MockupImages.CountAsync()).Should().Be(2);
        (await db.MockupTemplates.SingleAsync(t => t.Id == seed.TeeTemplateId)).UsageCount.Should().Be(1);
        (await db.MockupTemplates.SingleAsync(t => t.Id == seed.MugTemplateId)).UsageCount.Should().Be(1);

        db.ChangeTracker.Clear();
        var second = await CreateService(db).GenerateAllAsync(seed.BatchJobId);

        second.IsSuccess.Should().BeTrue();
        second.Value.GeneratedCount.Should().Be(0);
        second.Value.Images.Should().HaveCount(2);
        db.ChangeTracker.Clear();
        (await db.MockupImages.CountAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task GenerateAllAsync_AfterTemplatePrintAreaIsEdited_RegeneratesOnlyThatTemplatesMockups()
    {
        await using var db = CreateContext();
        var seed = SeedBatchJob(db);
        await CreateService(db).GenerateAllAsync(seed.BatchJobId);
        db.ChangeTracker.Clear();

        var tee = await db.MockupTemplates.SingleAsync(t => t.Id == seed.TeeTemplateId);
        tee.PrintAreaConfig = "{\"x\":500,\"y\":400,\"width\":600,\"height\":700}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).GenerateAllAsync(seed.BatchJobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedCount.Should().Be(1);
        result.Value.Images.Should().HaveCount(2);
        result.Value.Images.Should().ContainSingle(i => i.MockupTemplateId == seed.TeeTemplateId)
            .Which.MockupImageUrl.Should().Contain("c_scale,w_600,h_600/fl_layer_apply,g_north_west,x_500,y_450/", "the edited 600x700 print area");
        db.ChangeTracker.Clear();
        (await db.MockupImages.CountAsync()).Should().Be(3);
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MockupTemplateService CreateService(AppDbContext db)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var images = Mock.Of<IPublicImageService>();

        return new MockupTemplateService(
            currentUser.Object,
            new Repository<MockupTemplate>(db), new Repository<BatchJob>(db), new Repository<BatchJobProduct>(db),
            new Repository<Product>(db), new Repository<DesignImage>(db), new Repository<MockupImage>(db),
            images, new CloudinaryMockupCompositor(Microsoft.Extensions.Options.Options.Create(new CloudinaryOptions { Folder = "apcs" })),
            Mock.Of<IMockupMapService>(),
            db,
            new ApplyMockupTemplatesValidator(), new CreateMockupTemplateValidator(),
            new UpdateMockupTemplateValidator(), new GenerateMockupImageValidator(),
            new FakeTimeProvider(UtcNow),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MockupTemplateService>.Instance);
    }

    private static (Guid BatchJobId, Guid TeeTemplateId, Guid MugTemplateId) SeedBatchJob(AppDbContext db)
    {
        var now = UtcNow.UtcDateTime;
        var batchId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var teeTemplateId = Guid.NewGuid();
        var mugTemplateId = Guid.NewGuid();

        db.Batches.Add(new Batch { Id = batchId, UserId = UserId, Name = "B", InputMethod = "manual", Status = "processing", CreatedAt = now });
        db.BatchJobs.Add(new BatchJob
        {
            Id = jobId, UserId = UserId, BatchId = batchId, Name = "J", SourceFileType = "manual", Status = "completed",
            JobType = "design_generation", Config = $"{{\"mockupTemplateIds\":[\"{teeTemplateId:D}\",\"{mugTemplateId:D}\"]}}",
            Priority = "normal", TotalProducts = 2, ProcessedProducts = 2, FailedProducts = 0, CreatedAt = now
        });
        db.MockupTemplates.Add(new MockupTemplate
        {
            Id = teeTemplateId, UserId = UserId, Name = "Tee Front", ProductType = "tshirt",
            BaseImageUrl = "https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/mockup-templates/tee.jpg",
            PrintAreaConfig = "{\"x\":820,\"y\":640,\"width\":900,\"height\":1100}",
            OutputWidthPx = 2000, OutputHeightPx = 2000, IsActive = true, UsageCount = 0, CreatedAt = now, UpdatedAt = now
        });
        db.MockupTemplates.Add(new MockupTemplate
        {
            Id = mugTemplateId, UserId = UserId, Name = "Mug Front", ProductType = "mug",
            BaseImageUrl = "https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/mockup-templates/mug.jpg",
            PrintAreaConfig = "{\"x\":100,\"y\":100,\"width\":300,\"height\":300}",
            OutputWidthPx = 1200, OutputHeightPx = 1200, IsActive = true, UsageCount = 0, CreatedAt = now, UpdatedAt = now
        });

        foreach (var (productType, suffix) in new[] { ("tshirt", "tee"), ("mug", "mug") })
        {
            var productId = Guid.NewGuid();
            var rowId = Guid.NewGuid();
            db.Products.Add(new Product
            {
                Id = productId, UserId = UserId, BatchId = batchId, Name = $"Product {suffix}", ProductType = productType,
                ProcessingStatus = "completed", InputDescription = "desc", CreatedAt = now
            });
            db.BatchJobProducts.Add(new BatchJobProduct
            {
                Id = rowId, BatchJobId = jobId, BatchId = batchId, ProductId = productId, SequenceOrder = 1,
                Status = "completed", CreatedAt = now
            });
            db.DesignImages.Add(new DesignImage
            {
                Id = Guid.NewGuid(), ProductId = productId, AiPromptId = Guid.NewGuid(), BatchJobProductId = rowId,
                StorageKey = $"design-images/{suffix}/0", ImageGeneratorModel = "gemini-3.1-flash-image", StorageProvider = "cloudinary",
                ImageUrl = $"https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/design-images/{suffix}/0.png",
                ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png", FileSizeMb = 1.2m,
                ApprovalStatus = "pending", VariationIndex = 0, GenerationTimeSeconds = 3.5m, CreatedAt = now
            });
        }

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return (jobId, teeTemplateId, mugTemplateId);
    }

    private static (Guid TemplateId, Guid DesignImageId) Seed(AppDbContext db)
    {
        var now = UtcNow.UtcDateTime;
        var batchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var designImageId = Guid.NewGuid();

        db.Batches.Add(new Batch { Id = batchId, UserId = UserId, Name = "B", InputMethod = "manual", Status = "processing", CreatedAt = now });
        db.Products.Add(new Product
        {
            Id = productId, UserId = UserId, BatchId = batchId, Name = "Classic Tee", ProductType = "tshirt",
            ProcessingStatus = "queued", InputDescription = "desc", CreatedAt = now
        });
        db.MockupTemplates.Add(new MockupTemplate
        {
            Id = templateId, UserId = UserId, Name = "Tee Front", ProductType = "tshirt",
            BaseImageUrl = "https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/mockup-templates/tee.jpg",
            PrintAreaConfig = "{\"x\":820,\"y\":640,\"width\":900,\"height\":1100,\"unit\":\"px\"}",
            OutputWidthPx = 2000, OutputHeightPx = 2000, IsSystemTemplate = false, IsActive = true, UsageCount = 0,
            CreatedAt = now, UpdatedAt = now
        });
        db.DesignImages.Add(new DesignImage
        {
            Id = designImageId, ProductId = productId, AiPromptId = Guid.NewGuid(), StorageKey = "design-images/prompt-id/0",
            ImageGeneratorModel = "gemini-3.1-flash-image", StorageProvider = "cloudinary",
            ImageUrl = "https://res.cloudinary.com/demo/image/upload/v1700000000/apcs/design-images/prompt-id/0.png",
            ImageWidthPx = 1024, ImageHeightPx = 1024, FileFormat = "png", FileSizeMb = 1.2m,
            ApprovalStatus = "approved", VariationIndex = 0, GenerationTimeSeconds = 3.5m, CreatedAt = now
        });

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return (templateId, designImageId);
    }
}
