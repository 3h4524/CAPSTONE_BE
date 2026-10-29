using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Application.Features.DesignGeneration.Validators;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.BackgroundServices;
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
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, string _, bool _, bool _, CancellationToken _) =>
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
        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task ProcessBatchJobAsync_WhileAnImageIsMade_OnlyThatProductIsBeingGenerated()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 2, existingUsage: 0);
        // What the Batches page would read while the provider works on each product in turn.
        var seen = new List<string>();
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                seen.Add(string.Join(", ", db.Products.AsNoTracking().OrderBy(p => p.Name).Select(p => p.ProcessingStatus)));
                return ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m);
            });

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        seen.Should().Equal("generating_image, queued", "image_review_required, generating_image");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_TshirtDesign_AsksForAPlainBackdropAndStoresTheCutOut()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success([1, 2, 3], "image/jpeg", "test-model", 0.04m));
        var remover = new Mock<IDesignBackgroundRemover>();
        remover.Setup(x => x.RemoveBackground(It.IsAny<byte[]>())).Returns(Png);
        var storage = new Mock<IPublicImageService>();

        await CreateService(db, provider: provider, backgroundRemover: remover.Object, storage: storage).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.Is<string>(p => p.Contains("plain solid white background")),
            It.IsAny<string>(), false, false, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.UploadImageWithMetadataAsync(It.Is<UploadFileDto>(f => f.ContentType == "image/png"),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        db.ChangeTracker.Clear();
        (await db.DesignImages.SingleAsync()).FileFormat.Should().Be("png");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_IdeogramJob_AsksForATransparentDesignWithItsKeyAndCutsNothingOut()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, imageProvider: ImageGenerationProviders.Ideogram);
        var provider = new Mock<IImageGenerationProvider>();
        provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        provider.SetupGet(x => x.SupportsTransparentBackground).Returns(true);
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4-transparent", 0.03m));
        var remover = new Mock<IDesignBackgroundRemover>();

        await CreateService(db, provider: provider, backgroundRemover: remover.Object).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync("plain-key", It.Is<string>(p => !p.Contains("white background") && p.Contains("Output only the print-ready artwork")),
            "1:1", true, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        remover.Verify(x => x.RemoveBackground(It.IsAny<byte[]>()), Times.Never);
        db.ChangeTracker.Clear();
        var usage = await db.ApiUsageRecords.SingleAsync();
        usage.Provider.Should().Be(ImageGenerationProviders.Ideogram);
        usage.CostUsd.Should().Be(0.03m);
        (await db.BatchJobs.SingleAsync()).Status.Should().Be(BatchJobStatuses.Completed);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_PromptFromAPhraseTemplate_AllowsOnlyThatPhraseAsLettering()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var stored = await db.AiPrompts.SingleAsync();
        stored.SystemPrompt = "Create a print-ready {style} composition using the exact phrase \"{subject}\".";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.Is<string>(p => p.StartsWith("prompt 0\nLettering: the only text in the artwork is the exact quoted phrase")),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_PromptFromAnyOtherTemplate_DrawsMotifsAsPictures()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.Is<string>(p => p.StartsWith("prompt 0\nDraw every motif as a picture, never as a word or label.\n")),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_TemplateWithNegativeTerms_SendsThemAsThingsToAvoid()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await UseTemplateWithNegativeTermsAsync(db);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.Is<string>(p => p.Contains("\nAvoid: misspelled words, extra words.\n")),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_SellerWroteTheirOwnNegativeTerms_DoesNotAddTheTemplates()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await UseTemplateWithNegativeTermsAsync(db);
        var row = await db.BatchJobProducts.SingleAsync();
        row.CustomNegativeTerms = "blur";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        // Unbranded objects are a must, not one of the template's terms the Seller's own replace.
        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.Is<string>(p => !p.Contains("Avoid:") && p.Contains("\n" + ArtworkPromptRules.UnbrandedInstruction)),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_IdeogramLetteringDesign_IsSentTheTextPromptWithItsRules()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, imageProvider: ImageGenerationProviders.Ideogram);
        await UseLetteringTemplateAsync(db);
        var provider = new Mock<IImageGenerationProvider>();
        provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        provider.SetupGet(x => x.SupportsTransparentBackground).Returns(true);
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4-transparent", 0.03m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.Is<string>(p => p.StartsWith("prompt 0\nLettering:") && p.Contains("Output only the print-ready artwork")),
            It.IsAny<string>(), true, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_IdeogramProductWithNextToNothingToGoOn_LetsIdeogramFillOutTheTextPrompt()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, imageProvider: ImageGenerationProviders.Ideogram);
        await UseLetteringTemplateAsync(db, keywords: "coffee shirt, gift for mom");
        var provider = new Mock<IImageGenerationProvider>();
        provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        provider.SetupGet(x => x.SupportsTransparentBackground).Returns(true);
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4-transparent", 0.03m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.Is<string>(p => p.StartsWith("prompt 0\nLettering:")),
            It.IsAny<string>(), true, true, It.IsAny<CancellationToken>()), Times.Once);
        (await db.BatchJobLogs.CountAsync(l => l.EventType == "prompt_enriched")).Should().Be(1);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_IdeogramProductWithEnoughToGoOn_IsNotFilledOut()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, imageProvider: ImageGenerationProviders.Ideogram);
        await UseLetteringTemplateAsync(db);
        var provider = new Mock<IImageGenerationProvider>();
        provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_GeminiLetteringDesign_IsAlwaysSentAsText()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await UseLetteringTemplateAsync(db);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.Is<string>(p => p.StartsWith("prompt 0\nLettering:")),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_ProductWithItsOwnPrompt_DrawsNoWords_AndAvoidsAnyLettering_NotTheTemplatesTerms()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await UseTemplateWithNegativeTermsAsync(db);
        await UseCustomPromptAsync(db, "A red fox curled up asleep under a snowy pine tree, flat vector art.");
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.Is<string>(p => p.Contains("\nDraw every motif as a picture, never as a word or label.\n")
                && p.Contains("\n" + ArtworkPromptRules.UnbrandedInstruction + "\n")
                && p.Contains("\nAvoid: text, letters, words, brand logos, watermark, signature, product mockup.\n")
                && !p.Contains("misspelled words, extra words.")),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_ProductWithItsOwnPromptAndDesignText_KeepsTheWordsRight_WithoutTheTemplateLook()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await UseCustomPromptAsync(db, "A soft pastel watercolor seashell with thin elegant script lettering.", designText: "Salt Life");
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(),
            It.Is<string>(p => p.Contains("\nLettering: the only text in the artwork is the exact quoted phrase")
                && p.Contains("readable on both light and dark products.")
                && !p.Contains("vivid") && !p.Contains("featured illustration") && !p.Contains("double outline")
                && p.Contains("\nAvoid: misspelled words, extra words, distorted letters, brand logos, watermark, product mockup.\n")),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_IdeogramProductWithAShortOwnPrompt_LetsIdeogramFillItOut()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, imageProvider: ImageGenerationProviders.Ideogram);
        await UseCustomPromptAsync(db, "A fox.");
        var provider = new Mock<IImageGenerationProvider>();
        provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // The seeded product carries its own image prompt; its stored prompt was made from it, quoting the design text if any.
    [TestMethod]
    public async Task RegenerateProductAsync_KeepsTheEarlierDesigns_AndAddsNewOnesAfterThem_FromTheChangedPrompt()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 2);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var row = await db.BatchJobProducts.AsNoTracking().SingleAsync();
        var queue = new Mock<IDesignGenerationQueue>();

        var result = await CreateService(db, queue: queue.Object).RegenerateProductAsync(jobId, row.Id,
            new RegenerateProductRequestDto("A red fox curled up asleep under a snowy pine tree.", "Sleepy Fox"));

        result.IsSuccess.Should().BeTrue();
        queue.Verify(x => x.Enqueue(jobId), Times.Once);
        db.ChangeTracker.Clear();
        var product = await db.Products.SingleAsync();
        product.CustomPrompt.Should().Be("A red fox curled up asleep under a snowy pine tree.");
        product.DesiredDesignText.Should().Be("Sleepy Fox");
        var newPrompt = await db.AiPrompts.OrderByDescending(p => p.VersionNumber).FirstAsync();
        newPrompt.VersionNumber.Should().Be(2);
        newPrompt.BatchJobProductId.Should().Be(row.Id);
        newPrompt.GeneratedPrompt.Should().Be("A red fox curled up asleep under a snowy pine tree. The design prints the exact phrase \"Sleepy Fox\".");
        (await db.BatchJobs.SingleAsync()).Status.Should().Be(BatchJobStatuses.Queued);
        db.ChangeTracker.Clear();

        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));
        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        var images = await db.DesignImages.OrderBy(image => image.VariationIndex).ToListAsync();
        images.Select(image => image.VariationIndex).Should().Equal(1, 2, 3, 4);
        images.Skip(2).Should().OnlyContain(image => image.AiPromptId == newPrompt.Id);
        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.Is<string>(p => p.StartsWith(newPrompt.GeneratedPrompt)),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        var job = await db.BatchJobs.SingleAsync();
        job.Status.Should().Be(BatchJobStatuses.Completed);
        job.ProcessedProducts.Should().Be(1);
        job.FailedProducts.Should().Be(0);
        (await db.BatchJobProducts.SingleAsync()).Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        (await db.UsageStatistics.SingleAsync()).ImagesGenerated.Should().Be(4, "generating again uses quota again");
    }

    [TestMethod]
    public async Task RegenerateProductAsync_NoNewDesignMade_TheProductKeepsItsEarlierOnes_AndDoesNotFail()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 2);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;
        (await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto())).IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        var failing = new Mock<IImageGenerationProvider>();
        failing.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Failed("The provider is busy.", "test-model"));

        await CreateService(db, provider: failing).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(2);
        var row = await db.BatchJobProducts.SingleAsync();
        row.Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        row.ErrorMessage.Should().BeNull();
        var job = await db.BatchJobs.SingleAsync();
        job.Status.Should().Be(BatchJobStatuses.Completed);
        job.FailedProducts.Should().Be(0);
        (await db.BatchJobLogs.AnyAsync(log => log.EventType == "regenerate_failed")).Should().BeTrue();
    }

    [TestMethod]
    public async Task RegenerateProductAsync_ApprovedEarlierDesign_KeepsTheProductApproved_WhileTheNewOnesWaitForReview()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 1);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var first = await db.DesignImages.AsNoTracking().SingleAsync();
        await CreateService(db).SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto([first.Id], ApprovalStatuses.Approved));
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;
        (await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto())).IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.BatchJobProducts.SingleAsync()).Status.Should().Be(BatchJobProductStatuses.Approved);
        (await db.DesignImages.SingleAsync(image => image.VariationIndex == 2)).ApprovalStatus.Should().Be(ApprovalStatuses.Pending);
    }

    [TestMethod]
    public async Task RegenerateProductAsync_WhileTheJobRuns_IsRefused()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        var result = await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DesignGeneration.NotRegeneratable");
    }

    [TestMethod]
    public async Task RegenerateProductAsync_ProductSetBackToPending_IsGeneratedByTheNextJobInstead()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var product = await db.Products.SingleAsync();
        product.ProcessingStatus = BatchJobProductStatuses.Pending;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        var result = await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DesignGeneration.ProductMovedOn");
    }

    [TestMethod]
    public async Task RegenerateProductAsync_ImagePromptTooLong_IsRefusedBeforeAnythingChanges()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        var result = await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto(new string('x', 1001)));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
        (await db.Products.SingleAsync()).CustomPrompt.Should().BeNull();
    }

    [TestMethod]
    public async Task RegenerateProductAsync_AskedForFewerDesigns_MakesThatMany_AndTheJobKeepsItsOwnCount()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 2);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        (await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto(VariationCount: 1)))
            .IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.OrderBy(image => image.VariationIndex).Select(image => image.VariationIndex).ToListAsync())
            .Should().Equal(1, 2, 3);
        (await db.UsageStatistics.SingleAsync()).ImagesGenerated.Should().Be(3);
        var detail = await CreateService(db).GetJobAsync(jobId);
        detail.Value.VariationCount.Should().Be(2, "the job still started with two designs per product");
    }

    [TestMethod]
    public async Task RegenerateProductAsync_TemplateStyleAndInstructionsChosen_MakeTheNewPrompt()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var template = new DesignTemplate
        {
            Id = Guid.NewGuid(), Name = "Badge", Type = "system", ArtStyle = "vintage_badge", ExamplePrompts = "[]",
            BasePrompt = "A {style} badge of a mountain.", IsSystemTemplate = true, IsActive = true
        };
        var preset = new StyleArtPreset
        {
            Id = Guid.NewGuid(), Name = "Watercolor", Description = "d", StyleModifiers = "soft washes", Recommendations = "[]",
            IsSystemTemplate = true, IsActive = true, CreatedAt = UtcNow.UtcDateTime, UpdatedAt = UtcNow.UtcDateTime
        };
        db.DesignTemplates.Add(template);
        db.StyleArtPresets.Add(preset);
        var row = await db.BatchJobProducts.SingleAsync();
        row.CustomArtStyle = "pixel art";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).RegenerateProductAsync(jobId, row.Id, new RegenerateProductRequestDto(
            DesignTemplateId: template.Id, StyleArtPresetId: preset.Id.ToString(), Instructions: "warm sunset colors, add wildflowers"));

        result.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        var product = await db.Products.SingleAsync();
        product.DesignTemplateId.Should().Be(template.Id);
        product.StylePreset.Should().Be("Watercolor");
        var saved = await db.BatchJobProducts.SingleAsync();
        saved.CustomArtStyle.Should().BeNull("the chosen art style replaces the one typed in the prompt editor");
        saved.CustomInstructions.Should().Be("warm sunset colors, add wildflowers");
        var prompt = await db.AiPrompts.OrderByDescending(p => p.VersionNumber).FirstAsync();
        prompt.DesignTemplateId.Should().Be(template.Id);
        prompt.GeneratedPrompt.Should().Contain("Watercolor").And.Contain("warm sunset colors, add wildflowers")
            .And.NotContain("pixel art");
    }

    [TestMethod]
    public async Task RegenerateProductAsync_StyleRemovedAndInstructionsEmptied_UsesTheTemplatesStyleAndNoInstructions()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var job = await db.BatchJobs.SingleAsync();
        job.Config = "{\"variationCount\":1,\"aspectRatio\":\"1:1\",\"instructions\":\"keep it retro\"}";
        var product = await db.Products.SingleAsync();
        product.StylePreset = "Watercolor";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        (await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto(StyleArtPresetId: "", Instructions: "")))
            .IsSuccess.Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.Products.SingleAsync()).StylePreset.Should().BeNull();
        (await db.AiPrompts.OrderByDescending(p => p.VersionNumber).FirstAsync()).GeneratedPrompt.Should().NotContain("keep it retro");
    }

    [TestMethod]
    public async Task RegenerateProductAsync_UnknownTemplate_IsRefused()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        var result = await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto(DesignTemplateId: Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DesignGeneration.TemplateNotFound");
        db.ChangeTracker.Clear();
        (await db.AiPrompts.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    [DataRow("instructions")]
    [DataRow("style")]
    public async Task RegenerateProductAsync_InvalidChange_IsRefusedBeforeAnythingChanges(string field)
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;
        var request = field switch
        {
            "instructions" => new RegenerateProductRequestDto(Instructions: new string('i', 1001)),
            _ => new RegenerateProductRequestDto(StyleArtPresetId: "not-an-id")
        };

        var result = await CreateService(db).RegenerateProductAsync(jobId, rowId, request);

        result.IsSuccess.Should().BeFalse();
        db.ChangeTracker.Clear();
        (await db.AiPrompts.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task RegenerateProductAsync_UnknownStyle_IsRefused()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        var result = await CreateService(db).RegenerateProductAsync(jobId, rowId,
            new RegenerateProductRequestDto(StyleArtPresetId: Guid.NewGuid().ToString()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("DesignGeneration.StyleNotFound");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_RegenerateNoteThatCannotBeRead_MakesAsManyDesignsAsTheJob()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 2);
        var job = await db.BatchJobs.SingleAsync();
        job.Config = "{\"variationCount\":2,\"aspectRatio\":\"1:1\",\"regenerate\":{\"rowId\":\"nope\",\"variationCount\":1}}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_IdeogramShortOwnPromptWithInstructions_IsNotFilledOutByIdeogram()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, imageProvider: ImageGenerationProviders.Ideogram);
        await UseCustomPromptAsync(db, "A fox.");
        var row = await db.BatchJobProducts.SingleAsync();
        row.CustomInstructions = "curled up asleep under a snowy pine tree at dusk";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new Mock<IImageGenerationProvider>();
        provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(5)]
    public async Task RegenerateProductAsync_DesignCountOutOfRange_IsRefused(int variationCount)
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;

        var result = await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto(VariationCount: variationCount));

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
    }

    private const string CopyrightBlock =
        "Generation blocked by Ideogram: the image failed its safety or copyright check (it may show a real brand or character).";

    private static Mock<IImageGenerationProvider> ProviderReturning(params ImageGenerationResult[] results)
    {
        var provider = new Mock<IImageGenerationProvider>();
        var sequence = provider.SetupSequence(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()));
        foreach (var result in results) sequence = sequence.ReturnsAsync(result);
        return provider;
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_EveryDesignBlocked_TellsTheSellerWhyAndToChangeThePrompt()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 2);
        var provider = ProviderReturning(
            ImageGenerationResult.Blocked(CopyrightBlock, "ideogram-4"), ImageGenerationResult.Blocked(CopyrightBlock, "ideogram-4"));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        var row = await db.BatchJobProducts.SingleAsync();
        row.Status.Should().Be(BatchJobProductStatuses.Failed);
        row.ErrorMessage.Should().Be($"{CopyrightBlock} Change the prompt and generate it again.");
        var detail = await CreateService(db).GetJobAsync(jobId);
        detail.Value.Products.Single().ErrorMessage.Should().Contain("real brand or character");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_OneDesignBlocked_KeepsTheOther_AndSaysOneWasBlocked()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 2);
        var provider = ProviderReturning(
            ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m), ImageGenerationResult.Blocked(CopyrightBlock, "ideogram-4"));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        var row = await db.BatchJobProducts.SingleAsync();
        row.Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        row.ErrorMessage.Should().Be($"1 of 2 designs was blocked. {CopyrightBlock}");
        (await db.DesignImages.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_OneDesignBlockedAndTheOtherFailed_SaysNoneCouldBeMade()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 2);
        var provider = ProviderReturning(
            ImageGenerationResult.Failed("The provider is busy.", "ideogram-4"), ImageGenerationResult.Blocked("Prompt blocked by Ideogram: unsafe", "ideogram-4"));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.BatchJobProducts.SingleAsync()).ErrorMessage.Should()
            .Be("No design could be made: 1 of 2 were blocked. Prompt blocked by Ideogram: unsafe.");
    }

    [TestMethod]
    public async Task RegenerateProductAsync_NewDesignsBlocked_KeepsTheEarlierOnes_AndSaysTheNewOnesWereBlocked()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 1);
        await CreateService(db).ProcessBatchJobAsync(jobId);
        db.ChangeTracker.Clear();
        var rowId = (await db.BatchJobProducts.AsNoTracking().SingleAsync()).Id;
        (await CreateService(db).RegenerateProductAsync(jobId, rowId, new RegenerateProductRequestDto())).IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();

        await CreateService(db, provider: ProviderReturning(ImageGenerationResult.Blocked(CopyrightBlock, "ideogram-4")))
            .ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        var row = await db.BatchJobProducts.SingleAsync();
        row.Status.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        row.ErrorMessage.Should().Be($"1 of 1 new design was blocked. {CopyrightBlock}");
    }

    private static async Task UseCustomPromptAsync(AppDbContext db, string customPrompt, string? designText = null)
    {
        var product = await db.Products.SingleAsync();
        product.CustomPrompt = customPrompt;
        product.DesiredDesignText = designText;
        (await db.AiPrompts.SingleAsync()).SystemPrompt = designText is null
            ? customPrompt
            : $"{customPrompt} {APCS.Application.Features.BatchProductPrompts.Common.PromptDefaultsResolver.CustomPhraseSentence}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // The seeded product prints "Coffee Then Code" from a lettering template.
    private static async Task UseLetteringTemplateAsync(AppDbContext db, string keywords = "coffee, coding")
    {
        var template = new DesignTemplate
        {
            Id = Guid.NewGuid(), Name = "Bold Rally", Type = "system", ArtStyle = "bold_typography", ExamplePrompts = "[]",
            BasePrompt = "Create a {style} composition using the exact phrase \"{subject}\", use {keywords} as small supporting motifs.",
            IsSystemTemplate = true, IsActive = true
        };
        db.DesignTemplates.Add(template);
        var prompt = await db.AiPrompts.SingleAsync();
        prompt.DesignTemplateId = template.Id;
        prompt.SystemPrompt = template.BasePrompt;
        var product = await db.Products.SingleAsync();
        product.DesignTemplateId = template.Id;
        product.DesiredDesignText = "Coffee Then Code";
        product.MainKeywords = keywords;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // The seeded prompt is made from a template with negative terms of its own.
    private static async Task UseTemplateWithNegativeTermsAsync(AppDbContext db)
    {
        var template = new DesignTemplate
        {
            Id = Guid.NewGuid(), Name = "Bold Rally", Type = "system", BasePrompt = "b", ExamplePrompts = "[]",
            NegativePrompt = "misspelled words, extra words", IsSystemTemplate = true, IsActive = true
        };
        db.DesignTemplates.Add(template);
        (await db.AiPrompts.SingleAsync()).DesignTemplateId = template.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_JobNamingAProviderThatIsNotAvailable_FailsTheJob()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, imageProvider: "leonardo");

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.BatchJobs.SingleAsync()).Status.Should().Be(BatchJobStatuses.Failed);
        (await db.DesignImages.AnyAsync()).Should().BeFalse();
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

    [TestMethod]
    public async Task ProcessBatchJobAsync_RecordsWhereTheTimeOfEachImageWent()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 2, variations: 2, requireApproval: false);

        var timings = await db.BatchJobLogs.Where(l => l.BatchJobId == jobId && l.EventType == "image_timing").ToListAsync();

        timings.Should().HaveCount(4, "one entry for each image that was saved");
        timings.Should().OnlyContain(l => l.LogLevel == "debug" && l.BatchJobProductId != null && l.DurationMs >= 0);
        timings.Select(l => l.Message).Should().Contain(m => m.StartsWith("Image 1/2: provider ") && m.Contains("cut-out ") && m.Contains("upload "));
        using var details = System.Text.Json.JsonDocument.Parse(timings[0].Details!);
        details.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["providerMs", "cutOutMs", "uploadMs"]);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_ProviderThatDownloadsTheImage_RecordsBothParts_AndKeepsTheSeedAndPromptWithTheImage()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m) with
            {
                GenerateTime = TimeSpan.FromSeconds(30), DownloadTime = TimeSpan.FromSeconds(4), Seed = 42, ProviderPrompt = "{\"a\":1}"
            });

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        var timing = await db.BatchJobLogs.SingleAsync(l => l.EventType == "image_timing");
        timing.Message.Should().Contain("(generate 30.0s, download 4.0s)");
        using var details = System.Text.Json.JsonDocument.Parse(timing.Details!);
        details.RootElement.GetProperty("generateMs").GetInt64().Should().Be(30_000);
        details.RootElement.GetProperty("downloadMs").GetInt64().Should().Be(4_000);
        using var metadata = System.Text.Json.JsonDocument.Parse((await db.DesignImages.SingleAsync()).GenerationMetadata!);
        metadata.RootElement.GetProperty("seed").GetInt64().Should().Be(42);
        metadata.RootElement.GetProperty("providerPrompt").GetString().Should().Be("{\"a\":1}");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_ProviderRefusesTheAccount_AsksForNoFurtherImage_AndSaysWhyOnEveryProduct()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 3, existingUsage: 0);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.AccountRefused("Ideogram returned 402: Add credits.", "ideogram-4"));

        await CreateService(db, provider: provider).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync())
            .Should().OnlyContain(r => r.Status == BatchJobProductStatuses.Failed && r.ErrorMessage == "Ideogram returned 402: Add credits.");
        (await db.BatchJobLogs.CountAsync(l => l.EventType == "generation_stopped")).Should().Be(1);
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Failed);
    }

    // ---- One worker per job ----

    [TestMethod]
    public async Task ProcessBatchJobAsync_JobAnotherWorkerIsOn_IsLeftAlone()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 2, existingUsage: 0);
        await SetReportedAsync(db, jobId, minutesAgo: 1);

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(0);
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync())
            .Should().OnlyContain(r => r.Status == BatchJobProductStatuses.GeneratingImage, "the other worker still has them");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_JobItsWorkerAbandoned_IsResumed()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 2, existingUsage: 0);
        await SetReportedAsync(db, jobId, minutesAgo: 10);

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(2);
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.Completed);
        job.UpdatedAt.Should().Be(UtcNow.UtcDateTime, "the worker reports on the job while it works");
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_JobThatAlreadyFinished_IsNotRunAgain()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 2, variations: 1, requireApproval: false);

        // It can still be in a queue: every instance puts unfinished jobs back when it starts.
        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(2);
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Completed);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_WithoutAGeminiKey_FailsTheJobInsteadOfLeavingItQueued()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 2, existingUsage: 0, jobStatus: BatchJobStatuses.Queued);
        db.ApiKeys.RemoveRange(await db.ApiKeys.ToListAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await CreateService(db).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(BatchJobStatuses.Failed);
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync())
            .Should().OnlyContain(r => r.Status == BatchJobProductStatuses.Failed && r.ErrorMessage!.Contains("Gemini API key"));
        (await db.DesignImages.CountAsync()).Should().Be(0);
    }

    private static async Task SetReportedAsync(AppDbContext db, Guid jobId, int minutesAgo)
    {
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).UpdatedAt = UtcNow.AddMinutes(-minutesAgo).UtcDateTime;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // ---- Several images at once ----

    [TestMethod]
    public async Task ProcessBatchJobAsync_SeveralImagesAtOnce_MakesThemSideBySideAndSavesEveryOne()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 6, existingUsage: 0);
        var provider = new SlowProvider();

        await CreateService(db, provider: provider.Mock, maxConcurrentImages: 3).ProcessBatchJobAsync(jobId);

        provider.MostAtOnce.Should().BeInRange(2, 3, "images overlap, but never more than allowed");
        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(6);
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.Completed);
        (job.ProcessedProducts, job.FailedProducts, job.ProgressPercentage).Should().Be((6, 0, 100m));
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync())
            .Should().OnlyContain(r => r.Status == BatchJobProductStatuses.ImageReviewRequired);
        (await db.UsageStatistics.SingleAsync()).ImagesGenerated.Should().Be(6);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_VariationsOfOneProduct_AreMadeSideBySideWithinTheLimit()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0, variationCount: 4);
        var provider = new SlowProvider();

        await CreateService(db, provider: provider.Mock, maxConcurrentImages: 2).ProcessBatchJobAsync(jobId);

        provider.MostAtOnce.Should().Be(2);
        db.ChangeTracker.Clear();
        (await db.DesignImages.Select(i => i.VariationIndex).ToListAsync()).Should().BeEquivalentTo([1, 2, 3, 4]);
        (await db.BatchJobLogs.CountAsync(l => l.EventType == "image_timing")).Should().Be(4);
        (await db.UsageStatistics.SingleAsync()).ImagesGenerated.Should().Be(4);
    }

    [TestMethod]
    public async Task ProcessBatchJobAsync_SeveralAtOnceAndOneProductFails_TheOthersAreStillSaved()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 4, existingUsage: 0);
        var provider = new SlowProvider(fails: prompt => prompt.StartsWith("prompt 2"));

        await CreateService(db, provider: provider.Mock, maxConcurrentImages: 3).ProcessBatchJobAsync(jobId);

        db.ChangeTracker.Clear();
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.PartiallyCompleted);
        (job.ProcessedProducts, job.FailedProducts).Should().Be((4, 1));
        var rows = await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).OrderBy(r => r.SequenceOrder).ToListAsync();
        rows[2].Status.Should().Be(BatchJobProductStatuses.Failed);
        rows.Where((_, index) => index != 2).Should().OnlyContain(r => r.Status == BatchJobProductStatuses.ImageReviewRequired);
        (await db.DesignImages.CountAsync()).Should().Be(3);
        (await db.UsageStatistics.SingleAsync()).ImagesGenerated.Should().Be(3);
        (await db.BatchJobLogs.CountAsync(l => l.EventType == "generation_failed")).Should().Be(1);
    }

    [TestMethod]
    public async Task CancelAsync_WhileSeveralProductsAreBeingGenerated_FinishesThemAndFailsTheRest()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var db = CreateContext(databaseName);
        var jobId = Seed(db, productCount: 4, existingUsage: 0);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopPressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IImageGenerationProvider>();
        provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string prompt, string _, bool _, bool _, CancellationToken _) =>
            {
                if (prompt.StartsWith("prompt 0"))
                {
                    // The user presses Stop while the first two products are being generated, from their own request.
                    try
                    {
                        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        await using var requestDb = CreateContext(databaseName);
                        (await CreateService(requestDb).CancelAsync(jobId)).IsSuccess.Should().BeTrue();
                    }
                    finally
                    {
                        stopPressed.TrySetResult();
                    }
                }
                else
                {
                    secondStarted.TrySetResult();
                    await stopPressed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                return ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m);
            });

        await CreateService(db, provider: provider, maxConcurrentImages: 2).ProcessBatchJobAsync(jobId);

        provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        db.ChangeTracker.Clear();
        (await db.DesignImages.CountAsync()).Should().Be(2);
        var rows = await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).OrderBy(r => r.SequenceOrder).ToListAsync();
        rows.Take(2).Should().OnlyContain(r => r.Status == BatchJobProductStatuses.ImageReviewRequired);
        rows.Skip(2).Should().OnlyContain(r => r.Status == BatchJobProductStatuses.Failed && r.ErrorMessage == "Cancelled by the user.");
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.PartiallyCompleted);
        (job.ProcessedProducts, job.FailedProducts).Should().Be((4, 2));
        (await db.BatchJobLogs.CountAsync(l => l.EventType == "cancel_honored")).Should().Be(1);
    }

    // A provider that takes a moment for each image and remembers how many it was making at the same time.
    private sealed class SlowProvider
    {
        private readonly object sync = new();
        private int running;

        public SlowProvider(Func<string, bool>? fails = null)
        {
            Mock.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, string prompt, string _, bool _, bool _, CancellationToken cancellationToken) =>
                {
                    lock (sync) MostAtOnce = Math.Max(MostAtOnce, ++running);
                    try
                    {
                        await Task.Delay(100, cancellationToken);
                        return fails?.Invoke(prompt) == true
                            ? ImageGenerationResult.Failed("Gemini returned 500: boom", "test-model")
                            : ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m);
                    }
                    finally
                    {
                        lock (sync) running--;
                    }
                });
        }

        public Mock<IImageGenerationProvider> Mock { get; } = new();

        public int MostAtOnce { get; private set; }
    }

    // ---- Setting products back to pending ----

    [TestMethod]
    public async Task ResetProductsAsync_FailedProducts_GoBackToPendingAndTheirOldJobStopsRetryingThem()
    {
        await using var db = CreateContext();
        var jobId = await SeedFailedJobAsync(db, products: 2);
        var batchId = (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).BatchId;
        db.ChangeTracker.Clear();

        var result = await CreateService(db).ResetProductsAsync(batchId, new ResetProductsRequestDto());

        result.IsSuccess.Should().BeTrue();
        result.Value.ResetCount.Should().Be(2);
        db.ChangeTracker.Clear();
        (await db.Products.ToListAsync()).Should().OnlyContain(p => p.ProcessingStatus == BatchJobProductStatuses.Pending);
        // What the job recorded is kept: it still shows that both failed in it.
        (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).ToListAsync()).Should().OnlyContain(r => r.Status == BatchJobProductStatuses.Failed);

        db.ChangeTracker.Clear();
        (await CreateService(db).RetryFailedAsync(jobId)).Error.Code.Should().Be("DesignGeneration.NoFailedProducts");
    }

    [TestMethod]
    public async Task ResetProductsAsync_OneOfTwoFailedProducts_TheJobRetriesOnlyTheOther()
    {
        await using var db = CreateContext();
        var jobId = await SeedFailedJobAsync(db, products: 2);
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        var resetProductId = (await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).OrderBy(r => r.SequenceOrder).FirstAsync()).ProductId!.Value;
        db.ChangeTracker.Clear();

        (await CreateService(db).ResetProductsAsync(job.BatchId, new ResetProductsRequestDto([resetProductId]))).Value.ResetCount.Should().Be(1);
        db.ChangeTracker.Clear();
        var retry = await CreateService(db).RetryFailedAsync(jobId);

        retry.Value.QueuedProductCount.Should().Be(1);
        db.ChangeTracker.Clear();
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).FailedProducts.Should().Be(1, "the product that was reset still failed in this job");
        (await db.Products.FindAsync(resetProductId))!.ProcessingStatus.Should().Be(BatchJobProductStatuses.Pending);
    }

    [TestMethod]
    public async Task ResetProductsAsync_ProductWhoseDesignsWereAllRejected_GoesBackToPendingAndOneStillInReviewDoesNot()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 2, variations: 2, requireApproval: true);
        var batchId = (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).BatchId;
        var rows = await db.BatchJobProducts.Where(r => r.BatchJobId == jobId).OrderBy(r => r.SequenceOrder).ToListAsync();
        var rejected = await db.DesignImages.Where(i => i.BatchJobProductId == rows[0].Id).Select(i => i.Id).ToListAsync();
        db.ChangeTracker.Clear();
        await CreateService(db).SetImageApprovalAsync(jobId, new SetImageApprovalRequestDto(rejected, "rejected"));
        db.ChangeTracker.Clear();

        // Asked for by name, the product whose designs still wait for a decision is refused.
        var refused = await CreateService(db).ResetProductsAsync(batchId, new ResetProductsRequestDto([rows[1].ProductId!.Value]));
        db.ChangeTracker.Clear();
        var result = await CreateService(db).ResetProductsAsync(batchId, new ResetProductsRequestDto());

        refused.Error.Code.Should().Be("DesignGeneration.NotResettable");
        result.Value.ResetCount.Should().Be(1);
        db.ChangeTracker.Clear();
        (await db.Products.FindAsync(rows[0].ProductId))!.ProcessingStatus.Should().Be(BatchJobProductStatuses.Pending);
        (await db.Products.FindAsync(rows[1].ProductId))!.ProcessingStatus.Should().Be(BatchJobProductStatuses.ImageReviewRequired);
        (await db.DesignImages.CountAsync()).Should().Be(4, "the rejected designs are kept");
    }

    [TestMethod]
    public async Task ResetProductsAsync_WhileAJobOfTheBatchIsRunning_ReturnsMsg36()
    {
        await using var db = CreateContext();
        var jobId = Seed(db, productCount: 1, existingUsage: 0);
        var batchId = (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).BatchId;
        db.ChangeTracker.Clear();

        var result = await CreateService(db).ResetProductsAsync(batchId, new ResetProductsRequestDto());

        result.Error.Code.Should().Be("MSG36");
    }

    [TestMethod]
    public async Task ResetProductsAsync_NothingToResetOrAnUnknownProductOrBatch_ChangesNothing()
    {
        await using var db = CreateContext();
        var jobId = await SeedFinishedJobAsync(db, products: 1, variations: 1, requireApproval: false);
        var batchId = (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).BatchId;
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        (await service.ResetProductsAsync(batchId, new ResetProductsRequestDto())).Error.Code.Should().Be("DesignGeneration.NothingToReset");
        (await service.ResetProductsAsync(batchId, new ResetProductsRequestDto([Guid.NewGuid()]))).Error.Code.Should().Be("DesignGeneration.ProductNotFound");
        (await service.ResetProductsAsync(batchId, new ResetProductsRequestDto([]))).Error.Type.Should().Be(APCS.Common.Models.ErrorType.Validation);
        (await service.ResetProductsAsync(Guid.NewGuid(), new ResetProductsRequestDto())).Error.Code.Should().Be("DesignGeneration.BatchNotFound");
        db.ChangeTracker.Clear();
        (await db.Products.SingleAsync()).ProcessingStatus.Should().Be(BatchJobProductStatuses.Approved);
    }

    // A job in which every product failed, with the products marked failed as the worker leaves them.
    private static async Task<Guid> SeedFailedJobAsync(AppDbContext db, int products)
    {
        var jobId = Seed(db, productCount: products, existingUsage: 0, jobStatus: BatchJobStatuses.Failed, rowStatus: BatchJobProductStatuses.Failed);
        foreach (var product in await db.Products.ToListAsync())
            product.ProcessingStatus = BatchJobProductStatuses.Failed;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return jobId;
    }

    // Contexts made with the same name share one database, as two scopes of the application do.
    private static AppDbContext CreateContext(string? databaseName = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString()).Options);

    private static DesignGenerationService CreateService(
        AppDbContext db,
        IDesignGenerationQueue? queue = null,
        Mock<IImageGenerationProvider>? provider = null,
        IDesignBackgroundRemover? backgroundRemover = null,
        Mock<IPublicImageService>? storage = null,
        int maxConcurrentImages = 1)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("plain-key");

        if (provider is null)
        {
            provider = new Mock<IImageGenerationProvider>();
            provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "test-model", 0.04m));
        }
        // A test that does not name the provider is making Gemini images, as every job did before there was a choice.
        if (provider.Object.Provider is null)
            provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Gemini);

        storage ??= new Mock<IPublicImageService>();
        storage.Setup(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadFileDto _, string key, CancellationToken _) => new PublicImageUploadResult($"https://img.example/{key}.png", 1024, 768));

        return new DesignGenerationService(
            currentUser.Object,
            new Repository<Batch>(db), new Repository<BatchJob>(db), new Repository<BatchJobProduct>(db), new Repository<Product>(db),
            new Repository<AiPrompt>(db), new Repository<DesignImage>(db), new Repository<ApiUsageRecord>(db), new Repository<BatchJobLog>(db),
            new Repository<DesignTemplate>(db), new Repository<StyleArtPreset>(db),
            new ApiKeyRepository(db), credentials.Object, new SubscriptionRepository(db), new UsageStatisticRepository(db),
            [provider.Object], storage.Object, backgroundRemover ?? Mock.Of<IDesignBackgroundRemover>(), queue ?? Mock.Of<IDesignGenerationQueue>(),
            new BatchJobClaims(db, new FakeTimeProvider(UtcNow)), db,
            new FakeTimeProvider(UtcNow), new StartGenerationValidator(),
            Microsoft.Extensions.Options.Options.Create(new DesignGenerationSettings(maxConcurrentImages)));
    }

    private static Guid Seed(
        AppDbContext db,
        int productCount,
        int existingUsage,
        string jobStatus = BatchJobStatuses.Running,
        string rowStatus = BatchJobProductStatuses.GeneratingImage,
        int variationCount = 1,
        string? imageProvider = null)
    {
        var now = UtcNow.UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var batchId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        db.Batches.Add(new Batch { Id = batchId, UserId = UserId, Name = "B", InputMethod = "manual", Status = "processing", CreatedAt = now });
        db.BatchJobs.Add(new BatchJob
        {
            Id = jobId, UserId = UserId, BatchId = batchId, Name = "J", SourceFileType = "manual", Status = jobStatus,
            JobType = "design_generation", Config = imageProvider is null
                ? $"{{\"variationCount\":{variationCount},\"aspectRatio\":\"1:1\"}}"
                : $"{{\"variationCount\":{variationCount},\"aspectRatio\":\"1:1\",\"imageProvider\":\"{imageProvider}\"}}", Priority = "normal",
            TotalProducts = productCount,
            ProcessedProducts = jobStatus == BatchJobStatuses.Failed ? productCount : 0,
            FailedProducts = jobStatus == BatchJobStatuses.Failed ? productCount : 0, CreatedAt = now
        });
        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(), UserId = UserId, ServiceProvider = imageProvider ?? "gemini", KeyIdentifier = "k", KeyValueEncrypted = "enc",
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
