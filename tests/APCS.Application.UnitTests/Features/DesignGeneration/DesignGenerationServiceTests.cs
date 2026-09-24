using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Application.Features.DesignGeneration.Validators;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.DesignGeneration;

[TestClass]
public sealed class DesignGenerationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    private static StartGenerationRequestDto ValidRequest() => new(null, null, 1, "1:1");

    private sealed class Fixture
    {
        public required BatchJob Job { get; init; }
        public required List<BatchJobProduct> Rows { get; init; }
        public required List<AiPrompt> AddedPrompts { get; init; }
        public required Mock<IDesignGenerationQueue> Queue { get; init; }
        public required Mock<IUnitOfWork> UnitOfWork { get; init; }
        public required DesignGenerationService Service { get; init; }
    }

    private static Fixture CreateFixture(
        string jobStatus = BatchJobStatuses.Draft,
        string rowStatus = BatchJobProductStatuses.Pending,
        bool hasGeminiKey = true,
        bool hasOtherActiveJob = false,
        int imageGenerationQuota = 100,
        int alreadyUsedImages = 0)
    {
        var batchId = Guid.NewGuid();
        var job = new BatchJob
        {
            Id = Guid.NewGuid(), UserId = UserId, BatchId = batchId, Name = "Cats - design generation",
            SourceFileType = "manual", Status = jobStatus, JobType = "design_generation",
            Config = "{}", Priority = "normal", TotalProducts = 1, CreatedAt = UtcNow.UtcDateTime
        };
        var product = new Product
        {
            Id = Guid.NewGuid(), UserId = UserId, BatchId = batchId, Name = "Grumpy Cat",
            ProductType = "t-shirt", NicheCategory = "cats", ProcessingStatus = "pending", CreatedAt = UtcNow.UtcDateTime
        };
        var row = new BatchJobProduct
        {
            Id = Guid.NewGuid(), BatchJobId = job.Id, BatchId = batchId, ProductId = product.Id,
            SequenceOrder = 1, Status = rowStatus, CreatedAt = UtcNow.UtcDateTime
        };

        var jobs = new List<BatchJob> { job };
        if (hasOtherActiveJob)
            jobs.Add(new BatchJob
            {
                Id = Guid.NewGuid(), UserId = UserId, BatchId = batchId, Name = "Other job",
                SourceFileType = "manual", Status = BatchJobStatuses.Running, JobType = "design_generation",
                Config = "{}", Priority = "normal", CreatedAt = UtcNow.UtcDateTime
            });

        var batchRepo = MockRepo(new[] { new Batch { Id = batchId, UserId = UserId, Name = "Cats", Status = "draft", CreatedAt = UtcNow.UtcDateTime } });
        var jobRepo = MockRepo(jobs);
        var rowRepo = MockRepo(new[] { row });
        var productRepo = MockRepo(new[] { product });
        var templateRepo = MockRepo(Array.Empty<DesignTemplate>());
        var styleRepo = MockRepo(Array.Empty<StyleArtPreset>());

        var addedPrompts = new List<AiPrompt>();
        var promptRepo = new Mock<IRepository<AiPrompt>>();
        promptRepo.Setup(x => x.AddAsync(It.IsAny<AiPrompt>(), false, It.IsAny<CancellationToken>()))
            .Callback<AiPrompt, bool, CancellationToken>((p, _, _) => addedPrompts.Add(p)).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var apiKeys = new Mock<IApiKeyRepository>();
        var keys = hasGeminiKey
            ? new[]
            {
                new ApiKey
                {
                    Id = Guid.NewGuid(), UserId = UserId, ServiceProvider = "gemini", KeyIdentifier = "Default",
                    KeyValueEncrypted = "encrypted", AuthType = "api_key", IsActive = true, LastCheckSucceeded = true,
                    CreatedAt = UtcNow.UtcDateTime
                }
            }
            : [];
        apiKeys.Setup(x => x.ListOwnedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(keys);

        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("plain-key");

        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Name = "Starter", Tier = "starter", Description = "d",
            MonthlyPriceUsd = 9, MaxBatchSize = 50, MaxProductsPerMonth = 200, MaxConcurrentJobs = 2,
            ImageGenerationQuota = imageGenerationQuota, VideoGenerationQuota = 0, ApiCallQuota = 1000,
            StorageQuotaGb = 5, IsActive = true, SortOrder = 0, PlanFeatures = []
        };
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(), UserId = UserId, PlanId = plan.Id, Plan = plan,
            BillingCycle = "monthly", MonthlyPriceUsd = plan.MonthlyPriceUsd, Status = "active",
            StartDate = DateOnly.FromDateTime(UtcNow.UtcDateTime),
            RenewalDate = DateOnly.FromDateTime(UtcNow.UtcDateTime).AddMonths(1),
            AutoRenew = true, CreatedAt = UtcNow.UtcDateTime
        };
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(x => x.GetActiveWithPlanAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(subscription);

        var usage = new UsageStatistic
        {
            Id = Guid.NewGuid(), UserId = UserId,
            BillingPeriodStart = DateOnly.FromDateTime(UtcNow.UtcDateTime).AddMonths(-1),
            BillingPeriodEnd = DateOnly.FromDateTime(UtcNow.UtcDateTime).AddMonths(1),
            ImagesGenerated = alreadyUsedImages, CreatedAt = UtcNow.UtcDateTime
        };
        var usageStats = new Mock<IUsageStatisticRepository>();
        usageStats.Setup(x => x.GetCurrentPeriodAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(usage);

        var queue = new Mock<IDesignGenerationQueue>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var service = new DesignGenerationService(
            currentUser.Object, batchRepo.Object, jobRepo.Object, rowRepo.Object, productRepo.Object,
            promptRepo.Object, new Mock<IRepository<DesignImage>>().Object, new Mock<IRepository<ApiUsageRecord>>().Object,
            new Mock<IRepository<BatchJobLog>>().Object, templateRepo.Object, styleRepo.Object,
            apiKeys.Object, credentials.Object, subscriptions.Object, usageStats.Object,
            Mock.Of<IImageGenerationProvider>(), Mock.Of<IPublicImageService>(), queue.Object,
            unitOfWork.Object, new FakeTimeProvider(UtcNow), new StartGenerationValidator());

        return new Fixture
        {
            Job = job, Rows = [row], AddedPrompts = addedPrompts,
            Queue = queue, UnitOfWork = unitOfWork, Service = service
        };
    }

    private static Mock<IRepository<TEntity>> MockRepo<TEntity>(IEnumerable<TEntity> data) where TEntity : class
    {
        var query = data.AsQueryable().BuildMock();
        var repo = new Mock<IRepository<TEntity>>();
        repo.Setup(x => x.Query()).Returns(query);
        repo.Setup(x => x.UpdateAsync(It.IsAny<TEntity>(), false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.AddAsync(It.IsAny<TEntity>(), false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repo;
    }

    [TestMethod]
    public async Task StartAsync_JobNotDraft_ReturnsConflict()
    {
        var fixture = CreateFixture(jobStatus: BatchJobStatuses.Queued);
        var result = await fixture.Service.StartAsync(fixture.Job.Id, ValidRequest());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DesignGeneration.NotDraft");
    }

    [TestMethod]
    public async Task StartAsync_NoPendingRows_ReturnsConflict()
    {
        var fixture = CreateFixture(rowStatus: BatchJobProductStatuses.ImageReviewRequired);
        var result = await fixture.Service.StartAsync(fixture.Job.Id, ValidRequest());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Batches.NoPendingProducts");
    }

    [TestMethod]
    public async Task StartAsync_MissingGeminiKey_ReturnsMsg35()
    {
        var fixture = CreateFixture(hasGeminiKey: false);
        var result = await fixture.Service.StartAsync(fixture.Job.Id, ValidRequest());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("MSG35");
    }

    [TestMethod]
    public async Task StartAsync_AnotherActiveJobForSameBatch_ReturnsMsg36()
    {
        var fixture = CreateFixture(hasOtherActiveJob: true);
        var result = await fixture.Service.StartAsync(fixture.Job.Id, ValidRequest());
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("MSG36");
    }

    [TestMethod]
    public async Task StartAsync_NotEnoughQuotaForRequestedVariations_ReturnsMsg34()
    {
        // 1 pending row * 3 variations = 3 images needed, only 2 left in quota (5 - 3 already used).
        var fixture = CreateFixture(imageGenerationQuota: 5, alreadyUsedImages: 3);
        var result = await fixture.Service.StartAsync(fixture.Job.Id, new StartGenerationRequestDto(null, null, 3, "1:1"));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("MSG34");
    }

    [TestMethod]
    public async Task StartAsync_ValidDraftJob_SynthesizesPromptQueuesJobAndEnqueuesBackgroundWork()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.StartAsync(fixture.Job.Id, ValidRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.QueuedProductCount.Should().Be(1);
        fixture.Job.Status.Should().Be(BatchJobStatuses.Queued);
        fixture.Rows[0].Status.Should().Be(BatchJobProductStatuses.GeneratingImage);
        fixture.AddedPrompts.Should().ContainSingle(p => p.BatchJobProductId == fixture.Rows[0].Id);
        fixture.Queue.Verify(x => x.Enqueue(fixture.Job.Id), Times.Once);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
