using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Validators;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

/// <summary>
/// Runs the real <see cref="DesignGenerationService"/> against real repositories on an EF in-memory
/// database. Mock-based tests cannot catch EF change-tracking mistakes (identity conflicts, Add+Update
/// on a new row), which is exactly what broke multi-product jobs.
/// </summary>
[TestClass]
public sealed class DesignGenerationProcessingTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    // A valid 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [TestMethod]
    public async Task ProcessBatchJobAsync_ManyProducts_NoExistingUsageRow_SavesEveryImageAndCreatesOneUsageRow()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 3, existingUsage: 0);

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        var images = await db.DesignImages.ToListAsync();
        images.Should().HaveCount(3);
        images.Should().OnlyContain(i => i.StorageKey == $"design-images/{i.Id:N}");
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Completed);
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync())
            .Should().OnlyContain(r => r.Status == BatchJobProductStatuses.ImageReviewRequired);
        (await db.UsageStatistics.SingleAsync()).ImagesGenerated.Should().Be(3);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_ManyProducts_ExistingUsageRow_AddsToItWithoutTrackingConflict()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 3, existingUsage: 10);

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(3);
        (await db.UsageStatistics.SingleAsync()).ImagesGenerated.Should().Be(13);
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Completed);
    }

    [TestMethod]
    public async Task RetryFailedAsync_ResetsFailedProducts_RequeuesTheJob_AndTheRetryProducesImages()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 2, existingUsage: 0, jobStatus: BatchJobStatuses.Failed, rowStatus: BatchJobProductStatuses.Failed);
        var queue = new Mock<IDesignGenerationQueue>();
        var service = CreateService(db, queue.Object);

        var result = await service.RetryFailedAsync(jobId);

        result.IsSuccess.Should().BeTrue();
        result.Value.QueuedProductCount.Should().Be(2);
        queue.Verify(x => x.Enqueue(jobId), Times.Once);
        db.ChangeTracker.Clear();
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.Queued);
        job.FailedProducts.Should().Be(0);
        job.ProcessedProducts.Should().Be(0);
        var rows = await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync();
        rows.Should().OnlyContain(r => r.Status == BatchJobProductStatuses.GeneratingImage && r.ErrorMessage == null && r.RetryCount == 1);

        // The worker then processes exactly those rows using the prompts that already exist.
        db.ChangeTracker.Clear(); // production runs the worker in its own scope/DbContext
        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(2);
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Completed);
    }

    [TestMethod]
    public async Task RetryFailedAsync_WhileTheJobIsStillDraft_ReturnsConflict()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, jobStatus: BatchJobStatuses.Draft, rowStatus: BatchJobProductStatuses.Failed);

        var result = await CreateService(db).RetryFailedAsync(jobId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DesignGeneration.NotRetryable");
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DesignGenerationService CreateService(AppDbContext db, IDesignGenerationQueue? queue = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("plain-key");

        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        var storage = new Mock<IPublicImageService>();
        storage.Setup(x => x.UploadImageAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadFileDto _, string key, CancellationToken _) => $"https://img.example/{key}.png");

        return new DesignGenerationService(
            currentUser.Object,
            new Repository<Batch>(db), new Repository<BatchJob>(db), new Repository<BatchJobProduct>(db), new Repository<Product>(db),
            new Repository<AiPrompt>(db), new Repository<DesignImage>(db), new Repository<ApiUsageRecord>(db), new Repository<BatchJobLog>(db),
            new Repository<DesignTemplate>(db), new Repository<StyleArtPreset>(db),
            new ApiKeyRepository(db), credentials.Object, new SubscriptionRepository(db), new UsageStatisticRepository(db),
            provider.Object, storage.Object, queue ?? Mock.Of<IDesignGenerationQueue>(), db,
            new FakeTimeProvider(UtcNow), new StartGenerationValidator());
    }

    private static Guid Seed(
        AppDbContext db,
        int productCount,
        int existingUsage,
        string jobStatus = BatchJobStatuses.Running,
        string rowStatus = BatchJobProductStatuses.GeneratingImage)
    {
        var now = UtcNow.UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var batchId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        db.Batches.Add(new Batch { Id = batchId, UserId = UserId, Name = "B", InputMethod = "manual", Status = "processing", CreatedAt = now });
        db.BatchJobs.Add(new BatchJob
        {
            Id = jobId, UserId = UserId, BatchId = batchId, Name = "J", SourceFileType = "manual", Status = jobStatus,
            JobType = "design_generation", Config = "{\"variationCount\":1,\"aspectRatio\":\"1:1\"}", Priority = "normal",
            TotalProducts = productCount,
            ProcessedProducts = jobStatus == BatchJobStatuses.Failed ? productCount : 0,
            FailedProducts = jobStatus == BatchJobStatuses.Failed ? productCount : 0, CreatedAt = now
        });
        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(), UserId = UserId, ServiceProvider = "gemini", KeyIdentifier = "k", KeyValueEncrypted = "enc",
            AuthType = "api_key", IsActive = true, LastCheckSucceeded = true, CreatedAt = now
        });

        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Name = "Creator", Tier = "creator", Description = "d", MonthlyPriceUsd = 29, MaxBatchSize = 50,
            MaxProductsPerMonth = 500, MaxConcurrentJobs = 2, ImageGenerationQuota = 1000, VideoGenerationQuota = 0,
            ApiCallQuota = 1000, StorageQuotaGb = 5, IsActive = true, SortOrder = 0, PlanFeatures = []
        };
        db.SubscriptionPlans.Add(plan);
        // Annual plan renewing a year ahead: the case that produced a usage period in the future.
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), UserId = UserId, PlanId = plan.Id, BillingCycle = "annual", MonthlyPriceUsd = 29,
            Status = "active", StartDate = today, RenewalDate = today.AddYears(1), AutoRenew = true, CreatedAt = now
        });

        if (existingUsage > 0)
        {
            db.UsageStatistics.Add(new UsageStatistic
            {
                Id = Guid.NewGuid(), UserId = UserId, BillingPeriodStart = today.AddDays(-5), BillingPeriodEnd = today.AddDays(25),
                ImagesGenerated = existingUsage, CreatedAt = now
            });
        }

        for (var i = 0; i < productCount; i++)
        {
            var productId = Guid.NewGuid();
            var rowId = Guid.NewGuid();
            db.Products.Add(new Product
            {
                Id = productId, UserId = UserId, BatchId = batchId, Name = $"Product {i}", ProductType = "tshirt",
                ProcessingStatus = "queued", InputDescription = "desc", CreatedAt = now
            });
            db.BatchJobProducts.Add(new BatchJobProduct
            {
                Id = rowId, BatchJobId = jobId, BatchId = batchId, ProductId = productId, SequenceOrder = i + 1,
                Status = rowStatus, ErrorMessage = rowStatus == BatchJobProductStatuses.Failed ? "boom" : null, CreatedAt = now
            });
            db.AiPrompts.Add(new AiPrompt
            {
                Id = Guid.NewGuid(), ProductId = productId, BatchJobProductId = rowId, OriginalDescription = "d",
                SystemPrompt = "s", GeneratedPrompt = $"prompt {i}", VersionNumber = 1, CreatedAt = now
            });
        }

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return jobId;
    }
}
