using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.SystemStatistics;
using APCS.Application.Features.SystemStatistics.Dtos;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.SystemStatistics;

[TestClass]
public sealed class SystemStatisticsServiceTests
{
    private readonly Mock<IRepository<BatchJob>> _batchJobRepo = new();
    private readonly Mock<IRepository<DesignImage>> _designImageRepo = new();
    private readonly Mock<IRepository<MockupImage>> _mockupImageRepo = new();
    private readonly Mock<IRepository<PromoVideo>> _promoVideoRepo = new();
    private readonly Mock<IRepository<ListingContent>> _listingContentRepo = new();
    private readonly Mock<IRepository<EtsyUploadLog>> _etsyLogRepo = new();
    private readonly Mock<IRepository<PrintifyUploadLog>> _printifyLogRepo = new();
    private readonly Mock<IRepository<SocialMediaShare>> _socialMediaRepo = new();
    private readonly Mock<IRepository<BatchJobLog>> _batchJobLogRepo = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly SystemStatisticsService _sut;

    public SystemStatisticsServiceTests()
    {
        _sut = new SystemStatisticsService(
            _batchJobRepo.Object,
            _designImageRepo.Object,
            _mockupImageRepo.Object,
            _promoVideoRepo.Object,
            _listingContentRepo.Object,
            _etsyLogRepo.Object,
            _printifyLogRepo.Object,
            _socialMediaRepo.Object,
            _batchJobLogRepo.Object,
            _timeProvider
        );
    }

    [TestMethod]
    public async Task GetOverviewAsync_WhenDataExists_ReturnsAggregatedMetricsAndBreakdowns()
    {
        // Arrange (Default 30d: 2026-09-15 to 2026-10-15; previous: 2026-08-16 to 2026-09-15)
        var filter = new StatisticsFilterDto { TimeRange = "30d" };

        // 1. Batch jobs
        var batchJobs = new List<BatchJob>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 10) },
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 10) },
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 9, 20) },
            // Previous period
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 9, 1) }
        }.AsQueryable().BuildMock();
        _batchJobRepo.Setup(r => r.Query()).Returns(batchJobs);

        // 2. Images
        var images = new List<DesignImage>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 5), DeletedAt = null },
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 2), DeletedAt = null },
            // Deleted (should be ignored)
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 1), DeletedAt = DateTime.UtcNow },
            // Previous period
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 9, 1), DeletedAt = null }
        }.AsQueryable().BuildMock();
        _designImageRepo.Setup(r => r.Query()).Returns(images);

        // 3. Mockups
        var mockups = new List<MockupImage>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 4), DeletedAt = null }
        }.AsQueryable().BuildMock();
        _mockupImageRepo.Setup(r => r.Query()).Returns(mockups);

        // 4. Promo videos
        var videos = new List<PromoVideo>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 6), DeletedAt = null },
            // Previous
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 9, 2), DeletedAt = null }
        }.AsQueryable().BuildMock();
        _promoVideoRepo.Setup(r => r.Query()).Returns(videos);

        // 5. Listings
        var listings = new List<ListingContent>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 7) },
            // Previous
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 9, 3) }
        }.AsQueryable().BuildMock();
        _listingContentRepo.Setup(r => r.Query()).Returns(listings);

        // 6. Etsy Logs (1 success, 1 failed = 50%)
        var etsyLogs = new List<EtsyUploadLog>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 8), UploadStatus = "success" },
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 9), UploadStatus = "failed" }
        }.AsQueryable().BuildMock();
        _etsyLogRepo.Setup(r => r.Query()).Returns(etsyLogs);

        // 7. Printify Logs (1 success = 100%)
        var printifyLogs = new List<PrintifyUploadLog>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 10), UploadStatus = "success" }
        }.AsQueryable().BuildMock();
        _printifyLogRepo.Setup(r => r.Query()).Returns(printifyLogs);

        // 8. Social Media (1 posted = 100%)
        var socialLogs = new List<SocialMediaShare>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 11), ShareStatus = "posted" }
        }.AsQueryable().BuildMock();
        _socialMediaRepo.Setup(r => r.Query()).Returns(socialLogs);

        // 9. Recent Errors
        var errorLogs = new List<BatchJobLog>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 12), LogLevel = "error", EventType = "RateLimit", Message = "Too many requests to OpenAI" }
        }.AsQueryable().BuildMock();
        _batchJobLogRepo.Setup(r => r.Query()).Returns(errorLogs);

        // Act
        var result = await _sut.GetOverviewAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.Should().NotBeNull();

        // Metrics verification
        data.Metrics.TotalBatchJobs.Value.Should().Be(3);
        data.Metrics.TotalBatchJobs.PreviousValue.Should().Be(1);
        data.Metrics.TotalBatchJobs.GrowthPercentage.Should().Be(200m); // ((3-1)/1)*100 = 200%

        data.Metrics.ImagesGenerated.Value.Should().Be(2);
        data.Metrics.ImagesGenerated.PreviousValue.Should().Be(1);
        data.Metrics.ImagesGenerated.GrowthPercentage.Should().Be(100m);

        data.Metrics.VideosRendered.Value.Should().Be(1);
        data.Metrics.ListingsGenerated.Value.Should().Be(1);

        // Daily Jobs verification (grouped by date)
        data.DailyJobs.Should().HaveCount(2);
        data.DailyJobs.First(x => x.DateLabel == "2026-10-10").Count.Should().Be(2);

        // Pipeline Stages verification (Images: 2, Mockups: 1, Videos: 1, Listings: 1 -> Total = 5)
        data.PipelineStages.Should().HaveCount(4);
        data.PipelineStages.First(x => x.StageName == "Image Generation").Percentage.Should().Be(40m); // 2/5 = 40%

        // Integration Status verification
        data.IntegrationStatus.EtsyTotal.Should().Be(2);
        data.IntegrationStatus.EtsySuccessRate.Should().Be(50m);
        data.IntegrationStatus.PrintifySuccessRate.Should().Be(100m);
        data.IntegrationStatus.SocialMediaSuccessRate.Should().Be(100m);
        data.IntegrationStatus.FailedIntegrationsCount.Should().Be(1);

        // Recent Errors
        data.RecentErrors.Should().HaveCount(1);
        data.RecentErrors[0].ErrorType.Should().Be("RateLimit");
        data.RecentErrors[0].Message.Should().Be("Too many requests to OpenAI");
    }

    [TestMethod]
    public async Task GetOverviewAsync_WhenNoDataExists_HandlesEmptyStateGracefully()
    {
        // Arrange
        var filter = new StatisticsFilterDto { TimeRange = "30d" };

        _batchJobRepo.Setup(r => r.Query()).Returns(new List<BatchJob>().AsQueryable().BuildMock());
        _designImageRepo.Setup(r => r.Query()).Returns(new List<DesignImage>().AsQueryable().BuildMock());
        _mockupImageRepo.Setup(r => r.Query()).Returns(new List<MockupImage>().AsQueryable().BuildMock());
        _promoVideoRepo.Setup(r => r.Query()).Returns(new List<PromoVideo>().AsQueryable().BuildMock());
        _listingContentRepo.Setup(r => r.Query()).Returns(new List<ListingContent>().AsQueryable().BuildMock());
        _etsyLogRepo.Setup(r => r.Query()).Returns(new List<EtsyUploadLog>().AsQueryable().BuildMock());
        _printifyLogRepo.Setup(r => r.Query()).Returns(new List<PrintifyUploadLog>().AsQueryable().BuildMock());
        _socialMediaRepo.Setup(r => r.Query()).Returns(new List<SocialMediaShare>().AsQueryable().BuildMock());
        _batchJobLogRepo.Setup(r => r.Query()).Returns(new List<BatchJobLog>().AsQueryable().BuildMock());

        // Act
        var result = await _sut.GetOverviewAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        data.Metrics.TotalBatchJobs.Value.Should().Be(0);
        data.Metrics.TotalBatchJobs.GrowthPercentage.Should().Be(0);
        data.DailyJobs.Should().BeEmpty();
        data.PipelineStages.All(x => x.Count == 0 && x.Percentage == 0m).Should().BeTrue();
        data.IntegrationStatus.FailedIntegrationsCount.Should().Be(0);
        data.RecentErrors.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetOverviewAsync_WithCustomDatesAndDifferentTimeRanges_CalculatesCorrectly()
    {
        // Arrange: Custom explicit dates
        var filterCustom = new StatisticsFilterDto
        {
            StartDate = new DateTime(2026, 10, 1),
            EndDate = new DateTime(2026, 10, 5)
        };

        _batchJobRepo.Setup(r => r.Query()).Returns(new List<BatchJob>().AsQueryable().BuildMock());
        _designImageRepo.Setup(r => r.Query()).Returns(new List<DesignImage>().AsQueryable().BuildMock());
        _mockupImageRepo.Setup(r => r.Query()).Returns(new List<MockupImage>().AsQueryable().BuildMock());
        _promoVideoRepo.Setup(r => r.Query()).Returns(new List<PromoVideo>().AsQueryable().BuildMock());
        _listingContentRepo.Setup(r => r.Query()).Returns(new List<ListingContent>().AsQueryable().BuildMock());
        _etsyLogRepo.Setup(r => r.Query()).Returns(new List<EtsyUploadLog>().AsQueryable().BuildMock());
        _printifyLogRepo.Setup(r => r.Query()).Returns(new List<PrintifyUploadLog>().AsQueryable().BuildMock());
        _socialMediaRepo.Setup(r => r.Query()).Returns(new List<SocialMediaShare>().AsQueryable().BuildMock());
        _batchJobLogRepo.Setup(r => r.Query()).Returns(new List<BatchJobLog>().AsQueryable().BuildMock());

        // Act
        var result = await _sut.GetOverviewAsync(filterCustom, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Also test 7d, 90d, year branches
        foreach (var range in new[] { "7d", "90d", "year" })
        {
            var res = await _sut.GetOverviewAsync(new StatisticsFilterDto { TimeRange = range }, CancellationToken.None);
            res.IsSuccess.Should().BeTrue();
        }
    }

    [TestMethod]
    public async Task ExportCsvAsync_WhenCalled_ReturnsValidCsvBytes()
    {
        // Arrange
        var filter = new StatisticsFilterDto { TimeRange = "30d" };

        var batchJobs = new List<BatchJob>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 10) }
        }.AsQueryable().BuildMock();
        _batchJobRepo.Setup(r => r.Query()).Returns(batchJobs);

        var errors = new List<BatchJobLog>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 12), LogLevel = "error", EventType = "Timeout", Message = "Service timeout \"504\"" }
        }.AsQueryable().BuildMock();
        _batchJobLogRepo.Setup(r => r.Query()).Returns(errors);

        _designImageRepo.Setup(r => r.Query()).Returns(new List<DesignImage>().AsQueryable().BuildMock());
        _mockupImageRepo.Setup(r => r.Query()).Returns(new List<MockupImage>().AsQueryable().BuildMock());
        _promoVideoRepo.Setup(r => r.Query()).Returns(new List<PromoVideo>().AsQueryable().BuildMock());
        _listingContentRepo.Setup(r => r.Query()).Returns(new List<ListingContent>().AsQueryable().BuildMock());
        _etsyLogRepo.Setup(r => r.Query()).Returns(new List<EtsyUploadLog>().AsQueryable().BuildMock());
        _printifyLogRepo.Setup(r => r.Query()).Returns(new List<PrintifyUploadLog>().AsQueryable().BuildMock());
        _socialMediaRepo.Setup(r => r.Query()).Returns(new List<SocialMediaShare>().AsQueryable().BuildMock());

        // Act
        var result = await _sut.ExportCsvAsync(filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Length.Should().BeGreaterThan(0);

        var csvText = Encoding.UTF8.GetString(result.Value);
        csvText.Should().Contain("SYSTEM STATISTICS SUMMARY");
        csvText.Should().Contain("PIPELINE STAGE BREAKDOWN");
        csvText.Should().Contain("INTEGRATION HEALTH");
        csvText.Should().Contain("JOBS PROCESSED PER DAY");
        csvText.Should().Contain("RECENT OPERATIONAL ERRORS");
        csvText.Should().Contain("Timeout");
    }
}
