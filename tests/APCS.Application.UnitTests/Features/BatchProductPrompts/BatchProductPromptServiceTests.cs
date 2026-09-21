using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchProductPrompts;
using APCS.Application.Features.BatchProductPrompts.Dtos.Request;
using APCS.Application.Features.BatchProductPrompts.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.BatchProductPrompts;

[TestClass]
public sealed class BatchProductPromptServiceTests
{
    [TestMethod]
    public async Task GetEffectiveAsync_WhenOverrideStored_PrefersOverride()
    {
        var ids = TestIds();
        var service = CreateService(ids.UserId,
            rows: MockRows(new BatchJobProduct
            {
                Id = ids.RowId, BatchJobId = ids.BatchId, ProductId = ids.ProductId,
                Status = "pending", CustomSubject = "Space cat"
            }),
            batches: MockBatches(new BatchJob { Id = ids.BatchId, UserId = ids.UserId, Name = "B", Status = "draft", Config = "{}" }),
            products: MockProducts(new Product { Id = ids.ProductId, UserId = ids.UserId, Name = "Tee", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" }));

        var result = await service.GetEffectiveAsync(ids.RowId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Subject.Should().Be("Space cat");
        result.Value.IsCustomized.Should().BeTrue();
        result.Value.CanEdit.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetEffectiveAsync_WhenNoOverride_EqualsDefault()
    {
        var ids = TestIds();
        var service = CreateService(ids.UserId,
            rows: MockRows(new BatchJobProduct { Id = ids.RowId, BatchJobId = ids.BatchId, ProductId = ids.ProductId, Status = "pending" }),
            batches: MockBatches(new BatchJob { Id = ids.BatchId, UserId = ids.UserId, Name = "B", Status = "draft", Config = "{}" }),
            products: MockProducts(new Product { Id = ids.ProductId, UserId = ids.UserId, Name = "Tee", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" }));

        var effective = await service.GetEffectiveAsync(ids.RowId);
        var @default = await service.GetDefaultAsync(ids.RowId);

        effective.IsSuccess.Should().BeTrue();
        effective.Value.EffectivePrompt.Should().Be(@default.Value.EffectivePrompt);
        effective.Value.IsCustomized.Should().BeFalse();
    }

    [TestMethod]
    public async Task SaveAsync_WhenRowNotPending_ReturnsConflict()
    {
        var ids = TestIds();
        var service = CreateService(ids.UserId,
            rows: MockRows(new BatchJobProduct { Id = ids.RowId, BatchJobId = ids.BatchId, ProductId = ids.ProductId, Status = "running" }),
            batches: MockBatches(new BatchJob { Id = ids.BatchId, UserId = ids.UserId, Name = "B", Status = "draft", Config = "{}" }),
            products: MockProducts(new Product { Id = ids.ProductId, UserId = ids.UserId, Name = "Tee", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" }));

        var result = await service.SaveAsync(ids.RowId, ValidRequest());

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Conflict);
    }

    [TestMethod]
    public async Task SaveAsync_WhenTooLong_ReturnsValidationFailure()
    {
        var ids = TestIds();
        var service = CreateService(ids.UserId,
            rows: MockRows(new BatchJobProduct { Id = ids.RowId, BatchJobId = ids.BatchId, ProductId = ids.ProductId, Status = "pending" }),
            batches: MockBatches(new BatchJob { Id = ids.BatchId, UserId = ids.UserId, Name = "B", Status = "draft", Config = "{}" }),
            products: MockProducts(new Product { Id = ids.ProductId, UserId = ids.UserId, Name = "Tee", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" }));

        var result = await service.SaveAsync(ids.RowId, ValidRequest() with { Instructions = new string('x', 1000) });

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
    }

    [TestMethod]
    public async Task RestoreAsync_WhenCalled_ClearsBackToDefault()
    {
        var ids = TestIds();
        var stored = new BatchJobProduct
        {
            Id = ids.RowId, BatchJobId = ids.BatchId, ProductId = ids.ProductId,
            Status = "pending", CustomSubject = "Old"
        };
        var rows = new Mock<IRepository<BatchJobProduct>>();
        rows.Setup(r => r.Query()).Returns(new[] { stored }.AsQueryable().BuildMock());
        var service = CreateService(ids.UserId,
            rows: rows,
            batches: MockBatches(new BatchJob { Id = ids.BatchId, UserId = ids.UserId, Name = "B", Status = "draft", Config = "{}" }),
            products: MockProducts(new Product { Id = ids.ProductId, UserId = ids.UserId, Name = "Tee", ProductType = "tshirt", InputDescription = "d", ProcessingStatus = "pending" }));

        var result = await service.RestoreAsync(ids.RowId);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsCustomized.Should().BeFalse();
        stored.CustomSubject.Should().BeNull();
    }

    [TestMethod]
    public async Task SaveAsync_WhenUnauthenticated_ReturnsFailure()
    {
        var result = await CreateService(null).SaveAsync(Guid.NewGuid(), ValidRequest());

        result.IsSuccess.Should().BeFalse();
    }

    private static (Guid UserId, Guid BatchId, Guid RowId, Guid ProductId) TestIds() =>
        (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static UpdateBatchProductPromptRequestDto ValidRequest() =>
        new("Space cat", "Watercolor", "Calm", "", "Front print");

    private static BatchProductPromptService CreateService(
        Guid? userId,
        Mock<IRepository<BatchJobProduct>>? rows = null,
        Mock<IRepository<BatchJob>>? batches = null,
        Mock<IRepository<Product>>? products = null,
        Mock<IRepository<DesignTemplate>>? templates = null,
        Mock<IRepository<StyleArtPreset>>? styles = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(userId.HasValue);
        currentUser.SetupGet(user => user.UserId).Returns(userId);

        return new BatchProductPromptService(
            currentUser.Object,
            (rows ?? new Mock<IRepository<BatchJobProduct>>()).Object,
            (batches ?? new Mock<IRepository<BatchJob>>()).Object,
            (products ?? new Mock<IRepository<Product>>()).Object,
            (templates ?? Empty<DesignTemplate>()).Object,
            (styles ?? Empty<StyleArtPreset>()).Object,
            new UpdateBatchProductPromptValidator(),
            TimeProvider.System);
    }

    private static Mock<IRepository<T>> Empty<T>() where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Query()).Returns(new List<T>().AsQueryable().BuildMock());
        return repository;
    }

    private static Mock<IRepository<BatchJobProduct>> MockRows(params BatchJobProduct[] items)
    {
        var repository = new Mock<IRepository<BatchJobProduct>>();
        repository.Setup(r => r.Query()).Returns(items.AsQueryable().BuildMock());
        return repository;
    }

    private static Mock<IRepository<BatchJob>> MockBatches(params BatchJob[] items)
    {
        var repository = new Mock<IRepository<BatchJob>>();
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
