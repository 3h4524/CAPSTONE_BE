using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Batches.Dtos;
using APCS.Application.Features.Batches.Validators;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Batches;

[TestClass]
public sealed class BatchApprovalTests
{
    [TestMethod]
    public async Task ApproveAsync_WithPendingProducts_CreatesDraftDesignJobAndItems()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Spring", Status = "draft" };
        var pending = new List<Product>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Name = "Mug", ProcessingStatus = "pending" },
            new() { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Name = "T-shirt", ProcessingStatus = "pending" }
        };
        var batchRepo = MockRepository(new[] { batch });
        var productsRepo = MockRepository(pending);
        var jobs = new List<BatchJob>();
        var items = new List<BatchJobProduct>();
        var jobRepo = new Mock<IRepository<BatchJob>>();
        jobRepo.Setup(x => x.AddAsync(It.IsAny<BatchJob>(), false, It.IsAny<CancellationToken>()))
            .Callback<BatchJob, bool, CancellationToken>((job, _, _) => jobs.Add(job)).Returns(Task.CompletedTask);
        var jobProductRepo = new Mock<IRepository<BatchJobProduct>>();
        jobProductRepo.Setup(x => x.AddAsync(It.IsAny<BatchJobProduct>(), false, It.IsAny<CancellationToken>()))
            .Callback<BatchJobProduct, bool, CancellationToken>((item, _, _) => items.Add(item)).Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = CreateService(userId, batchRepo, productsRepo, jobRepo, jobProductRepo, unitOfWork);

        var result = await service.ApproveAsync(batch.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.QueuedProductCount.Should().Be(2);
        // Draft, not Queued: the Seller still has to configure mock-ups/prompt overrides and click
        // "Start Image Job" (DesignGenerationService.StartAsync) before generation actually begins.
        jobs.Should().ContainSingle().Which.Status.Should().Be("draft");
        jobs.Single().JobType.Should().Be("design_generation");
        items.Should().HaveCount(2).And.OnlyContain(item => item.BatchJobId == jobs[0].Id && item.Status == "pending");
        pending.Should().OnlyContain(product => product.ProcessingStatus == "pending");
        batch.Status.Should().Be("draft");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ApproveAsync_WithoutPendingProducts_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Empty", Status = "draft" };
        var batchRepo = MockRepository(new[] { batch });
        var productsRepo = MockRepository(Array.Empty<Product>());
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(userId, batchRepo, productsRepo, unitOfWork: unitOfWork);

        var result = await service.ApproveAsync(batch.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Batches.NoPendingProducts");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_ChangesBatchNameAndDefaults()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Old", Status = "draft" };
        var batchRepo = MockRepository(new[] { batch });
        var productRepo = MockRepository(Array.Empty<Product>());
        var service = CreateService(userId, batchRepo, productRepo);

        var result = await service.UpdateAsync(batch.Id, new("New", "Description", "hiking", "tshirt"));

        result.IsSuccess.Should().BeTrue();
        batch.Name.Should().Be("New");
        batch.DefaultNiche.Should().Be("hiking");
        result.Value.Description.Should().Be("Description");
        result.Value.Status.Should().Be("draft");
    }

    [TestMethod]
    [DataRow("pending", "completed", "draft", DisplayName = "A product is waiting to run")]
    [DataRow("approved", "completed", "completed", DisplayName = "Every product has run")]
    [DataRow("failed", "partially_completed", "completed", DisplayName = "A failed product is not waiting to run")]
    [DataRow("image_review_required", "completed", "completed", DisplayName = "A product in review is not waiting to run")]
    [DataRow("queued", "queued", "processing", DisplayName = "A job is queued")]
    [DataRow("pending", "running", "processing", DisplayName = "A job is running, whatever else waits")]
    public async Task ListAsync_WorksOutTheStatusFromJobsAndProducts(string productStatus, string jobStatus, string expected)
    {
        var userId = Guid.NewGuid();
        // The stored status disagrees with the expected one, to show it is not what is returned.
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Spring", Status = expected == "completed" ? "draft" : "completed" };
        var product = new Product { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Name = "Mug", ProcessingStatus = productStatus };
        batch.Products.Add(product);
        var jobRepo = MockRepository(new[] { new BatchJob { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Status = jobStatus } });
        var service = CreateService(userId, MockRepository(new[] { batch }), MockRepository(new[] { product }), jobRepo: jobRepo);

        var result = await service.ListAsync();

        result.Value.Should().ContainSingle().Which.Status.Should().Be(expected);
    }

    [TestMethod]
    public async Task ListAsync_IgnoresDeletedProductsAndJobs()
    {
        var userId = Guid.NewGuid();
        var deletedAt = DateTime.Parse("2026-09-17T00:00:00Z").ToUniversalTime();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Spring", Status = "processing" };
        var kept = new Product { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Name = "Mug", ProcessingStatus = "approved" };
        var deleted = new Product { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Name = "Tee", ProcessingStatus = "pending", DeletedAt = deletedAt };
        batch.Products.Add(kept);
        batch.Products.Add(deleted);
        var jobRepo = MockRepository(new[] { new BatchJob { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Status = "running", DeletedAt = deletedAt } });
        var service = CreateService(userId, MockRepository(new[] { batch }), MockRepository(new[] { kept, deleted }), jobRepo: jobRepo);

        var result = await service.ListAsync();

        result.Value.Should().ContainSingle().Which.Status.Should().Be("completed");
        result.Value[0].ProductCount.Should().Be(1);
    }

    [TestMethod]
    public async Task ListAsync_WithoutProducts_IsADraft()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Empty", Status = "completed" };
        var service = CreateService(userId, MockRepository(new[] { batch }), MockRepository(Array.Empty<Product>()));

        var result = await service.ListAsync();

        result.Value.Should().ContainSingle().Which.Status.Should().Be("draft");
    }

    [TestMethod]
    public async Task DeleteBatchAsync_WithActiveJob_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Active", Status = "processing" };
        var batchRepo = MockRepository(new[] { batch });
        var productRepo = MockRepository(Array.Empty<Product>());
        var jobRepo = MockRepository(new[] { new BatchJob { Id = Guid.NewGuid(), UserId = userId, BatchId = batch.Id, Status = "queued" } });
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(userId, batchRepo, productRepo, jobRepo: jobRepo, unitOfWork: unitOfWork);

        var result = await service.DeleteBatchAsync(batch.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Batches.ActiveJob");
        batch.DeletedAt.Should().BeNull();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteBatchAsync_WithoutActiveJob_SoftDeletesBatch()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Finished", Status = "completed" };
        var batchRepo = MockRepository(new[] { batch });
        var productRepo = MockRepository(Array.Empty<Product>());
        var jobRepo = MockRepository(Array.Empty<BatchJob>());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = CreateService(userId, batchRepo, productRepo, jobRepo: jobRepo, unitOfWork: unitOfWork);

        var result = await service.DeleteBatchAsync(batch.Id);

        result.IsSuccess.Should().BeTrue();
        batch.DeletedAt.Should().NotBeNull();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AddProductAsync_WithDesignText_StoresItTrimmedAndReturnsIt()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Spring", Status = "draft" };
        var productRepo = MockRepository(Array.Empty<Product>());
        Product? added = null;
        productRepo.Setup(x => x.AddAsync(It.IsAny<Product>(), false, It.IsAny<CancellationToken>()))
            .Callback<Product, bool, CancellationToken>((product, _, _) => added = product).Returns(Task.CompletedTask);
        var service = CreateService(userId, MockRepository(new[] { batch }), productRepo);

        var result = await service.AddProductAsync(batch.Id,
            new SaveProductDto("Mountain Sunrise", "tshirt", "Hikers", ["mountain"], null, null, "  GO HIGHER  "));

        result.IsSuccess.Should().BeTrue();
        added!.DesiredDesignText.Should().Be("GO HIGHER");
        result.Value.DesignText.Should().Be("GO HIGHER");
    }

    [TestMethod]
    public async Task AddProductAsync_WithACustomPrompt_StoresItTrimmedAndReturnsIt()
    {
        var userId = Guid.NewGuid();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = userId, Name = "Spring", Status = "draft" };
        var productRepo = MockRepository(Array.Empty<Product>());
        Product? added = null;
        productRepo.Setup(x => x.AddAsync(It.IsAny<Product>(), false, It.IsAny<CancellationToken>()))
            .Callback<Product, bool, CancellationToken>((product, _, _) => added = product).Returns(Task.CompletedTask);
        var service = CreateService(userId, MockRepository(new[] { batch }), productRepo);

        var result = await service.AddProductAsync(batch.Id,
            new SaveProductDto("Mountain Sunrise", "tshirt", "Hikers", ["mountain"], null, null, CustomPrompt: "  A mountain badge.  "));

        result.IsSuccess.Should().BeTrue();
        added!.CustomPrompt.Should().Be("A mountain badge.");
        result.Value.CustomPrompt.Should().Be("A mountain badge.");
    }

    [TestMethod]
    public void SaveProductValidator_CustomPromptOverOneThousandCharacters_IsRejected()
    {
        var validation = new SaveProductValidator().Validate(
            new SaveProductDto("Mountain Sunrise", "tshirt", "Hikers", ["mountain"], null, null, CustomPrompt: new string('x', 1001)));

        validation.Errors.Should().ContainSingle(error => error.PropertyName == nameof(SaveProductDto.CustomPrompt));
    }

    [TestMethod]
    public void SaveProductValidator_DesignTextOverOneHundredCharacters_IsRejected()
    {
        var validation = new SaveProductValidator().Validate(
            new SaveProductDto("Mountain Sunrise", "tshirt", "Hikers", ["mountain"], null, null, new string('x', 101)));

        validation.Errors.Should().ContainSingle(error => error.PropertyName == nameof(SaveProductDto.DesignText));
    }

    private static Mock<IRepository<TEntity>> MockRepository<TEntity>(IEnumerable<TEntity> data) where TEntity : class
    {
        var query = data.AsQueryable().BuildMock();
        var repository = new Mock<IRepository<TEntity>>();
        repository.Setup(x => x.Query()).Returns(query);
        repository.Setup(x => x.UpdateAsync(It.IsAny<TEntity>(), false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static BatchService CreateService(
        Guid userId,
        Mock<IRepository<Batch>> batchRepo,
        Mock<IRepository<Product>> productRepo,
        Mock<IRepository<BatchJob>>? jobRepo = null,
        Mock<IRepository<BatchJobProduct>>? itemRepo = null,
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        var accounts = new Mock<IAccountService>();
        accounts.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInfoDto(userId, "seller@example.com", "Seller", true, true, "active", null));
        accounts.Setup(x => x.GetRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { AuthConstants.UserRole });

        return new BatchService(
            currentUser.Object, accounts.Object, batchRepo.Object,
            (jobRepo ?? MockRepository(Array.Empty<BatchJob>())).Object,
            (itemRepo ?? new Mock<IRepository<BatchJobProduct>>()).Object,
            productRepo.Object, unitOfWork?.Object ?? new Mock<IUnitOfWork>().Object,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-09-18T00:00:00Z")),
            new SaveBatchValidator(), new SaveProductValidator());
    }
}
