using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
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
    public async Task ApplyAsync_WhenBatchNotDraft_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var batches = MockBatches(new BatchJob { Id = batchId, UserId = userId, Name = "B", Status = "Queued", Config = "{}" });

        var result = await CreateService(userId, batches: batches)
            .ApplyAsync(batchId, new ApplyMockupTemplatesRequestDto([Guid.NewGuid()]));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
    }

    [TestMethod]
    public async Task ApplyAsync_WhenEmptySelection_ReturnsFailure()
    {
        var result = await CreateService(Guid.NewGuid())
            .ApplyAsync(Guid.NewGuid(), new ApplyMockupTemplatesRequestDto([]));

        result.IsSuccess.Should().BeFalse();
    }

    private static MockupTemplateService CreateService(
        Guid? userId,
        Mock<IRepository<MockupTemplate>>? templates = null,
        Mock<IRepository<BatchJob>>? batches = null,
        Mock<IRepository<BatchJobProduct>>? rows = null,
        Mock<IRepository<Product>>? products = null)
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
            new ApplyMockupTemplatesValidator(),
            TimeProvider.System);
    }

    private static Mock<IRepository<MockupTemplate>> TemplateRepository(params MockupTemplate[] items)
    {
        var repository = new Mock<IRepository<MockupTemplate>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        return repository;
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
}
