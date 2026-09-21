using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Batches;
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
    public async Task ApproveAsync_WithPendingProducts_CreatesQueuedDesignJobAndItems()
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
        jobs.Should().ContainSingle().Which.Status.Should().Be("queued");
        jobs.Single().JobType.Should().Be("design_generation");
        items.Should().HaveCount(2).And.OnlyContain(item => item.BatchJobId == jobs[0].Id && item.Status == "pending");
        pending.Should().OnlyContain(product => product.ProcessingStatus == "queued");
        batch.Status.Should().Be("processing");
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
            .ReturnsAsync(new AccountInfoDto(userId, "seller@example.com", "Seller", true, true));
        accounts.Setup(x => x.GetRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { AuthConstants.UserRole });

        return new BatchService(
            currentUser.Object, accounts.Object, batchRepo.Object,
            (jobRepo ?? new Mock<IRepository<BatchJob>>()).Object,
            (itemRepo ?? new Mock<IRepository<BatchJobProduct>>()).Object,
            productRepo.Object, unitOfWork?.Object ?? new Mock<IUnitOfWork>().Object,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-09-18T00:00:00Z")),
            new SaveBatchValidator(), new SaveProductValidator());
    }
}
