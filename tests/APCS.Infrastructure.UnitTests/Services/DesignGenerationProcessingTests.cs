using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
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
        images.Should().OnlyContain(i => i.ImageWidthPx == 1024 && i.ImageHeightPx == 768, "Gemini's JPEG size comes from the upload");
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

    // ---- Approval (SRS 3.5.12) ----

    // A finished job with `products` products and `variations` images each, started with the given approval mode.
    private static async Task<Guid> SeedFinishedJobAsync(AppDbContext db, int products, int variations, bool? requireApproval)
    {
        var jobId = Seed(db, productCount: products, existingUsage: 0);
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        var approval = requireApproval is null ? string.Empty : $",\"requireApproval\":{requireApproval.Value.ToString().ToLowerInvariant()}";
        job.Config = $"{{\"variationCount\":{variations},\"aspectRatio\":\"1:1\"{approval}}}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        return jobId;
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_WhenApprovalIsRequired_LeavesImagesPendingAndProductsInReview()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 2, variations: 2, requireApproval: true);

        (await db.DesignImages.ToListAsync()).Should().HaveCount(4).And.OnlyContain(i => i.ApprovalStatus == "pending");
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync())
            .Should().OnlyContain(r => r.Status == BatchJobProductStatuses.ImageReviewRequired);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_WhenApprovalIsAutomatic_ApprovesImagesAndProducts()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 2, variations: 2, requireApproval: false);

        (await db.DesignImages.ToListAsync()).Should().OnlyContain(i => i.ApprovalStatus == "approved");
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync()).Should().OnlyContain(r => r.Status == BatchJobProductStatuses.Approved);
        (await db.Products.ToListAsync()).Should().OnlyContain(p => p.ProcessingStatus == BatchJobProductStatuses.Approved);

        var detail = await CreateService(db).GetJobAsync(jobId);
        detail.Value.Counters.Completed.Should().Be(2, "approved products count as completed");
        detail.Value.RequireApproval.Should().BeFalse();
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_ForAJobFromBeforeApprovalExisted_KeepsTheOldBehavior()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 1, variations: 1, requireApproval: null);

        (await db.DesignImages.SingleAsync()).ApprovalStatus.Should().Be("pending");
        (await db.BatchJobProducts.SingleAsync(r => r.BatchJobId == jobId)).Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        (await CreateService(db).GetJobAsync(jobId)).Value.RequireApproval.Should().BeNull();
    }

    [TestMethod]
    public async Task SetImageApprovalAsync_ApprovesTheChosenImages_AndTheirProductFollows()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 2, variations: 2, requireApproval: true);
        var rows = await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).OrderBy(r => r.SequenceOrder).ToListAsync();
        var firstProductImage = await db.DesignImages.Where(i => i.BatchJobProductId == rows[0].Id).OrderBy(i => i.VariationIndex).FirstAsync();
        db.ChangeTracker.Clear(); // the request is handled in its own scope in production

        var result = await CreateService(db).SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto([firstProductImage.Id], "approved"));

        result.IsSuccess.Should().BeTrue();
        (result.Value.UpdatedCount, result.Value.Approved, result.Value.Pending, result.Value.Rejected).Should().Be((1, 1, 3, 0));
        db.ChangeTracker.Clear();
        (await db.DesignImages.FindAsync(firstProductImage.Id))!.ApprovalStatus.Should().Be("approved");
        var after = await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).OrderBy(r => r.SequenceOrder).ToListAsync();
        after[0].Status.Should().Be(BatchJobProductStatuses.Approved);
        after[1].Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        (await db.Products.FindAsync(rows[0].ProductId))!.ProcessingStatus.Should().Be(BatchJobProductStatuses.Approved);
    }

    [TestMethod]
    public async Task SetImageApprovalAsync_TakingBackTheLastApprovedImage_PutsTheProductBackInReview()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 1, variations: 2, requireApproval: true);
        var service = CreateService(db);
        await service.SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto(null, "approved"));
        db.ChangeTracker.Clear();
        (await db.BatchJobProducts.SingleAsync(r => r.BatchJobId == jobId)).Status.Should().Be(BatchJobProductStatuses.Approved);

        var images = await db.DesignImages.ToListAsync();
        db.ChangeTracker.Clear();
        var rejectBoth = await service.SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto(images.Select(i => i.Id).ToList(), "rejected"));

        rejectBoth.Value.Rejected.Should().Be(2);
        db.ChangeTracker.Clear();
        (await db.BatchJobProducts.SingleAsync(r => r.BatchJobId == jobId)).Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
    }

    [TestMethod]
    public async Task SetImageApprovalAsync_WithoutImageIds_OnlyChangesPendingImages()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 1, variations: 3, requireApproval: true);
        var images = await db.DesignImages.OrderBy(i => i.VariationIndex).ToListAsync();
        db.ChangeTracker.Clear();
        var service = CreateService(db);
        await service.SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto([images[0].Id], "rejected"));
        db.ChangeTracker.Clear();

        var result = await service.SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto(null, "approved"));

        result.Value.UpdatedCount.Should().Be(2, "the rejected image is not overridden by approve all");
        db.ChangeTracker.Clear();
        (await db.DesignImages.FindAsync(images[0].Id))!.ApprovalStatus.Should().Be("rejected");
        (await db.DesignImages.CountAsync(i => i.ApprovalStatus == "approved")).Should().Be(2);
    }

    [TestMethod]
    public async Task SetImageApprovalAsync_WhileTheJobIsStillRunning_ReturnsConflict()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, jobStatus: BatchJobStatuses.Running);

        var result = await CreateService(db).SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto(null, "approved"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DesignGeneration.ApprovalNotAvailable");
    }

    [TestMethod]
    public async Task SetImageApprovalAsync_WithAnImageOfAnotherJobOrABadStatus_IsRejected()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 1, variations: 1, requireApproval: true);
        var service = CreateService(db);

        var unknownImage = await service.SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto([Guid.NewGuid()], "approved"));
        var badStatus = await service.SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto(null, "maybe"));
        var emptyList = await service.SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto([], "approved"));
        var otherJob = await service.SetImageApprovalAsync(Guid.NewGuid(), new SetImageApprovalRequestDto(null, "approved"));

        unknownImage.Error.Code.Should().Be("DesignGeneration.ImageNotFound");
        badStatus.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
        emptyList.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
        otherJob.Error.Code.Should().Be("DesignGeneration.JobNotFound");
        db.ChangeTracker.Clear();
        (await db.DesignImages.SingleAsync()).ApprovalStatus.Should().Be("pending", "no failed call changed anything");
    }

    [TestMethod]
    public async Task CancelAsync_BeforeTheWorkerRuns_FailsEveryProductAsCancelledAndGeneratesNothing()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 3, existingUsage: 0, jobStatus: BatchJobStatuses.Queued);

        var cancel = await CreateService(db).CancelAsync(jobId);
        db.ChangeTracker.Clear(); // production runs the worker in its own scope/DbContext
        await CreateService(db).ProcessBatchJobAsync(jobId);

        cancel.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(0);
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.Failed);
        job.FailedProducts.Should().Be(3);
        job.ProcessedProducts.Should().Be(3);
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync())
            .Should().OnlyContain(r => r.Status == BatchJobProductStatuses.Failed && r.ErrorMessage == "Cancelled by the user.");
        (await db.Products.ToListAsync()).Should().OnlyContain(p => p.ProcessingStatus == BatchJobProductStatuses.Failed);
        (await db.BatchJobLogs.Where(l => l.BatchJobId == jobId).Select(l => l.EventType).ToListAsync())
            .Should().Contain(["cancel_requested", "cancel_honored"]);
    }

    [TestMethod]
    public async Task CancelAsync_WhileAProductIsBeingGenerated_FinishesThatProductAndFailsTheRest()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 3, existingUsage: 0);
        DesignGenerationService? service = null;
        var calls = 0;
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, string _, CancellationToken _) =>
            {
                if (calls++ == 0) await service!.CancelAsync(jobId); // the user presses Stop during the first product
                return ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m);
            });
        service = CreateService(db, provider: provider);

        await service.ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(1);
        var rows = await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).OrderBy(r => r.SequenceOrder).ToListAsync();
        rows[0].Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        rows.Skip(1).Should().OnlyContain(r => r.Status == BatchJobProductStatuses.Failed && r.ErrorMessage == "Cancelled by the user.");
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.PartiallyCompleted);
        job.FailedProducts.Should().Be(2);
        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CancelAsync_ThenRetryFailed_GeneratesTheCancelledProductsInsteadOfStoppingAgain()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 2, existingUsage: 0, jobStatus: BatchJobStatuses.Queued);
        await CreateService(db).CancelAsync(jobId);
        db.ChangeTracker.Clear();
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();

        var retry = await CreateService(db).RetryFailedAsync(jobId);
        db.ChangeTracker.Clear();
        await CreateService(db).ProcessBatchJobAsync(jobId);

        retry.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(2);
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Completed);
    }

    [TestMethod]
    public async Task CancelAsync_WhenTheJobAlreadyFinished_ReturnsConflict()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, jobStatus: BatchJobStatuses.Completed, rowStatus: BatchJobProductStatuses.ImageReviewRequired);

        var result = await CreateService(db).CancelAsync(jobId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DesignGeneration.NotCancellable");
        db.ChangeTracker.Clear();
        (await db.BatchJobLogs.AnyAsync()).Should().BeFalse();
    }

    [TestMethod]
    public async Task CancelAsync_AskedTwice_RecordsOnlyOneRequest()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, jobStatus: BatchJobStatuses.Running);
        var service = CreateService(db);

        await service.CancelAsync(jobId);
        var second = await service.CancelAsync(jobId);

        second.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        (await db.BatchJobLogs.CountAsync(l => l.EventType == "cancel_requested")).Should().Be(1);
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

    [TestMethod]
    public async Task ProcessBatchJobAsync_TshirtDesign_AsksForAPlainBackdropAndStoresTheCutOut()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success([1, 2, 3], "image/jpeg", "test-model", 0.04m));
        var remover = new Mock<IDesignBackgroundRemover>();
        remover.Setup(x => x.RemoveBackground(It.IsAny<byte[]>())).Returns(Png);
        var storage = new Mock<IPublicImageService>();

        await CreateService(db, provider: provider, backgroundRemover: remover.Object, storage: storage).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.Is<string>(p => p.Contains("plain solid white background")),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.UploadImageWithMetadataAsync(It.Is<UploadFileDto>(f => f.ContentType == "image/png"),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        db.ChangeTracker.Clear();
        (await db.DesignImages.SingleAsync()).FileFormat.Should().Be("png");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_WhenTheCutOutFails_KeepsTheImageAsGenerated()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var remover = new Mock<IDesignBackgroundRemover>();
        remover.Setup(x => x.RemoveBackground(It.IsAny<byte[]>())).Throws(new InvalidOperationException("unreadable"));

        await CreateService(db, backgroundRemover: remover.Object).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(1);
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Completed);
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DesignGenerationService CreateService(
        AppDbContext db,
        IDesignGenerationQueue? queue = null,
        Mock<IImageGenerationProvider>? provider = null,
        IDesignBackgroundRemover? backgroundRemover = null,
        Mock<IPublicImageService>? storage = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("plain-key");

        if (provider is null)
        {
            provider = new Mock<IImageGenerationProvider>();
            provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));
        }

        storage ??= new Mock<IPublicImageService>();
        storage.Setup(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadFileDto _, string key, CancellationToken _) => new PublicImageUploadResult($"https://img.example/{key}.png", 1024, 768));

        return new DesignGenerationService(
            currentUser.Object,
            new Repository<Batch>(db), new Repository<BatchJob>(db), new Repository<BatchJobProduct>(db), new Repository<Product>(db),
            new Repository<AiPrompt>(db), new Repository<DesignImage>(db), new Repository<ApiUsageRecord>(db), new Repository<BatchJobLog>(db),
            new Repository<DesignTemplate>(db), new Repository<StyleArtPreset>(db),
            new ApiKeyRepository(db), credentials.Object, new SubscriptionRepository(db), new UsageStatisticRepository(db),
            provider.Object, storage.Object, backgroundRemover ?? Mock.Of<IDesignBackgroundRemover>(), queue ?? Mock.Of<IDesignGenerationQueue>(), db,
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
