using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.SystemStatistics.Dtos;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.SystemStatistics;

public sealed class SystemStatisticsService : ISystemStatisticsService
{
    private readonly IRepository<BatchJob> _batchJobRepo;
    private readonly IRepository<DesignImage> _designImageRepo;
    private readonly IRepository<MockupImage> _mockupImageRepo;
    private readonly IRepository<PromoVideo> _promoVideoRepo;
    private readonly IRepository<ListingContent> _listingContentRepo;
    private readonly IRepository<EtsyUploadLog> _etsyLogRepo;
    private readonly IRepository<PrintifyUploadLog> _printifyLogRepo;
    private readonly IRepository<SocialMediaShare> _socialMediaRepo;
    private readonly IRepository<BatchJobLog> _batchJobLogRepo;
    private readonly TimeProvider _timeProvider;

    public SystemStatisticsService(
        IRepository<BatchJob> batchJobRepo,
        IRepository<DesignImage> designImageRepo,
        IRepository<MockupImage> mockupImageRepo,
        IRepository<PromoVideo> promoVideoRepo,
        IRepository<ListingContent> listingContentRepo,
        IRepository<EtsyUploadLog> etsyLogRepo,
        IRepository<PrintifyUploadLog> printifyLogRepo,
        IRepository<SocialMediaShare> socialMediaRepo,
        IRepository<BatchJobLog> batchJobLogRepo,
        TimeProvider? timeProvider = null)
    {
        _batchJobRepo = batchJobRepo;
        _designImageRepo = designImageRepo;
        _mockupImageRepo = mockupImageRepo;
        _promoVideoRepo = promoVideoRepo;
        _listingContentRepo = listingContentRepo;
        _etsyLogRepo = etsyLogRepo;
        _printifyLogRepo = printifyLogRepo;
        _socialMediaRepo = socialMediaRepo;
        _batchJobLogRepo = batchJobLogRepo;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<SystemStatisticsOverviewDto>> GetOverviewAsync(StatisticsFilterDto filter, CancellationToken cancellationToken = default)
    {
        var (startDate, endDate, previousStartDate, previousEndDate) = ResolveDateRanges(filter);

        // 1. Metric Cards (Batch jobs, Images, Videos, Listings)
        var currentBatchJobs = await _batchJobRepo.Query()
            .Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate)
            .CountAsync(cancellationToken);
        var previousBatchJobs = await _batchJobRepo.Query()
            .Where(x => x.CreatedAt >= previousStartDate && x.CreatedAt < startDate)
            .CountAsync(cancellationToken);

        var currentImages = await _designImageRepo.Query()
            .Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate && x.DeletedAt == null)
            .CountAsync(cancellationToken);
        var previousImages = await _designImageRepo.Query()
            .Where(x => x.CreatedAt >= previousStartDate && x.CreatedAt < startDate && x.DeletedAt == null)
            .CountAsync(cancellationToken);

        var currentVideos = await _promoVideoRepo.Query()
            .Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate && x.DeletedAt == null)
            .CountAsync(cancellationToken);
        var previousVideos = await _promoVideoRepo.Query()
            .Where(x => x.CreatedAt >= previousStartDate && x.CreatedAt < startDate && x.DeletedAt == null)
            .CountAsync(cancellationToken);

        var currentListings = await _listingContentRepo.Query()
            .Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate)
            .CountAsync(cancellationToken);
        var previousListings = await _listingContentRepo.Query()
            .Where(x => x.CreatedAt >= previousStartDate && x.CreatedAt < startDate)
            .CountAsync(cancellationToken);

        var metrics = new StatisticsMetricCardsDto
        {
            TotalBatchJobs = CreateMetric(currentBatchJobs, previousBatchJobs),
            ImagesGenerated = CreateMetric(currentImages, previousImages),
            VideosRendered = CreateMetric(currentVideos, previousVideos),
            ListingsGenerated = CreateMetric(currentListings, previousListings)
        };

        // 2. Daily Jobs Bar Chart
        var dailyJobCounts = await _batchJobRepo.Query()
            .Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate && x.CreatedAt != null)
            .GroupBy(x => x.CreatedAt!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var dailyJobs = dailyJobCounts
            .OrderBy(x => x.Date)
            .Select(x => new DailyJobsPointDto
            {
                DateLabel = x.Date.ToString("yyyy-MM-dd"),
                Count = x.Count
            })
            .ToList();

        // 3. Pipeline Stage Breakdown
        var mockupsCount = await _mockupImageRepo.Query()
            .Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate && x.DeletedAt == null)
            .CountAsync(cancellationToken);

        long totalPipeline = currentImages + mockupsCount + currentVideos + currentListings;

        var pipelineStages = new List<PipelineStageBreakdownDto>
        {
            new() { StageName = "Image Generation", Count = currentImages, Percentage = CalculatePercentage(currentImages, totalPipeline) },
            new() { StageName = "Mock-up Rendering", Count = mockupsCount, Percentage = CalculatePercentage(mockupsCount, totalPipeline) },
            new() { StageName = "Video Rendering", Count = currentVideos, Percentage = CalculatePercentage(currentVideos, totalPipeline) },
            new() { StageName = "Listing Generation", Count = currentListings, Percentage = CalculatePercentage(currentListings, totalPipeline) }
        };

        // 4. Integration Status
        var etsyTotal = await _etsyLogRepo.Query().Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate).CountAsync(cancellationToken);
        var etsySuccess = await _etsyLogRepo.Query().Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate && x.UploadStatus == "success").CountAsync(cancellationToken);
        var previousEtsyTotal = await _etsyLogRepo.Query().Where(x => x.CreatedAt >= previousStartDate && x.CreatedAt < startDate).CountAsync(cancellationToken);

        var printifyTotal = await _printifyLogRepo.Query().Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate).CountAsync(cancellationToken);
        var printifySuccess = await _printifyLogRepo.Query().Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate && x.UploadStatus == "success").CountAsync(cancellationToken);
        var previousPrintifyTotal = await _printifyLogRepo.Query().Where(x => x.CreatedAt >= previousStartDate && x.CreatedAt < startDate).CountAsync(cancellationToken);

        var socialTotal = await _socialMediaRepo.Query().Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate).CountAsync(cancellationToken);
        var socialSuccess = await _socialMediaRepo.Query().Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate && x.ShareStatus == "posted").CountAsync(cancellationToken);
        var previousSocialTotal = await _socialMediaRepo.Query().Where(x => x.CreatedAt >= previousStartDate && x.CreatedAt < startDate).CountAsync(cancellationToken);

        int failedIntegrations = (etsyTotal - etsySuccess) + (printifyTotal - printifySuccess) + (socialTotal - socialSuccess);

        var integrationStatus = new IntegrationStatusDto
        {
            EtsyTotal = etsyTotal,
            EtsySuccessRate = etsyTotal > 0 ? Math.Round((etsySuccess * 100m) / etsyTotal, 1) : 100m,
            EtsyGrowthPercentage = CreateMetric(etsyTotal, previousEtsyTotal).GrowthPercentage,
            PrintifyTotal = printifyTotal,
            PrintifySuccessRate = printifyTotal > 0 ? Math.Round((printifySuccess * 100m) / printifyTotal, 1) : 100m,
            PrintifyGrowthPercentage = CreateMetric(printifyTotal, previousPrintifyTotal).GrowthPercentage,
            SocialMediaTotal = socialTotal,
            SocialMediaSuccessRate = socialTotal > 0 ? Math.Round((socialSuccess * 100m) / socialTotal, 1) : 100m,
            SocialMediaGrowthPercentage = CreateMetric(socialTotal, previousSocialTotal).GrowthPercentage,
            FailedIntegrationsCount = Math.Max(0, failedIntegrations)
        };

        // 5. Recent Errors
        var recentErrors = await _batchJobLogRepo.Query()
            .Include(x => x.BatchJob)
            .Where(x => x.LogLevel.ToLower() == "error" || x.EventType.ToLower().Contains("error"))
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new SystemErrorLogDto
            {
                Id = x.Id,
                Timestamp = x.CreatedAt ?? DateTime.UtcNow,
                ErrorType = x.EventType,
                Message = x.Message,
                Source = x.BatchJob != null ? x.BatchJob.Name : "Batch Pipeline"
            })
            .ToListAsync(cancellationToken);

        var result = new SystemStatisticsOverviewDto
        {
            Metrics = metrics,
            DailyJobs = dailyJobs,
            PipelineStages = pipelineStages,
            IntegrationStatus = integrationStatus,
            RecentErrors = recentErrors
        };

        return Result.Success(result);
    }

    public async Task<Result<byte[]>> ExportCsvAsync(StatisticsFilterDto filter, CancellationToken cancellationToken = default)
    {
        var overviewResult = await GetOverviewAsync(filter, cancellationToken);
        if (overviewResult.IsFailure)
        {
            return Result.Failure<byte[]>(overviewResult.Error);
        }

        var data = overviewResult.Value;
        var csv = new StringBuilder();

        // Section 1: Overview Metrics
        csv.AppendLine("=== SYSTEM STATISTICS SUMMARY ===");
        csv.AppendLine("Metric,Current Value,Previous Period Value,Growth (%)");
        csv.AppendLine($"Total Batch Jobs,{data.Metrics.TotalBatchJobs.Value},{data.Metrics.TotalBatchJobs.PreviousValue},{data.Metrics.TotalBatchJobs.GrowthPercentage}%");
        csv.AppendLine($"Images Generated,{data.Metrics.ImagesGenerated.Value},{data.Metrics.ImagesGenerated.PreviousValue},{data.Metrics.ImagesGenerated.GrowthPercentage}%");
        csv.AppendLine($"Videos Rendered,{data.Metrics.VideosRendered.Value},{data.Metrics.VideosRendered.PreviousValue},{data.Metrics.VideosRendered.GrowthPercentage}%");
        csv.AppendLine($"Listings Generated,{data.Metrics.ListingsGenerated.Value},{data.Metrics.ListingsGenerated.PreviousValue},{data.Metrics.ListingsGenerated.GrowthPercentage}%");
        csv.AppendLine();

        // Section 2: Pipeline Stages
        csv.AppendLine("=== PIPELINE STAGE BREAKDOWN ===");
        csv.AppendLine("Stage,Count,Percentage (%)");
        foreach (var stage in data.PipelineStages)
        {
            csv.AppendLine($"\"{stage.StageName}\",{stage.Count},{stage.Percentage}%");
        }
        csv.AppendLine();

        // Section 3: Integration Status
        csv.AppendLine("=== INTEGRATION HEALTH ===");
        csv.AppendLine("Integration,Total Requests,Success Rate (%),Growth (%)");
        csv.AppendLine($"Etsy Listings,{data.IntegrationStatus.EtsyTotal},{data.IntegrationStatus.EtsySuccessRate}%,{data.IntegrationStatus.EtsyGrowthPercentage}%");
        csv.AppendLine($"Printify Products,{data.IntegrationStatus.PrintifyTotal},{data.IntegrationStatus.PrintifySuccessRate}%,{data.IntegrationStatus.PrintifyGrowthPercentage}%");
        csv.AppendLine($"Social Media Shares,{data.IntegrationStatus.SocialMediaTotal},{data.IntegrationStatus.SocialMediaSuccessRate}%,{data.IntegrationStatus.SocialMediaGrowthPercentage}%");
        csv.AppendLine($"Total Failed Integrations,{data.IntegrationStatus.FailedIntegrationsCount},-, -");
        csv.AppendLine();

        // Section 4: Daily Jobs Volume
        csv.AppendLine("=== JOBS PROCESSED PER DAY ===");
        csv.AppendLine("Date,Jobs Count");
        foreach (var dj in data.DailyJobs)
        {
            csv.AppendLine($"{dj.DateLabel},{dj.Count}");
        }
        csv.AppendLine();

        // Section 5: Recent Errors
        csv.AppendLine("=== RECENT OPERATIONAL ERRORS ===");
        csv.AppendLine("Timestamp,Error Type,Source,Message");
        foreach (var err in data.RecentErrors)
        {
            var msgEscaped = err.Message.Replace("\"", "\"\"");
            csv.AppendLine($"{err.Timestamp:yyyy-MM-dd HH:mm:ss},\"{err.ErrorType}\",\"{err.Source}\",\"{msgEscaped}\"");
        }

        // Return UTF-8 bytes with BOM so Excel displays properly
        var preamble = Encoding.UTF8.GetPreamble();
        var contentBytes = Encoding.UTF8.GetBytes(csv.ToString());
        var fileBytes = new byte[preamble.Length + contentBytes.Length];
        Buffer.BlockCopy(preamble, 0, fileBytes, 0, preamble.Length);
        Buffer.BlockCopy(contentBytes, 0, fileBytes, preamble.Length, contentBytes.Length);

        return Result.Success(fileBytes);
    }

    private (DateTime StartDate, DateTime EndDate, DateTime PreviousStartDate, DateTime PreviousEndDate) ResolveDateRanges(StatisticsFilterDto filter)
    {
        DateTime startDate;
        DateTime endDate;
        int days;

        if (filter.StartDate.HasValue && filter.EndDate.HasValue)
        {
            startDate = filter.StartDate.Value;
            endDate = filter.EndDate.Value;
            days = Math.Max(1, (int)(endDate - startDate).TotalDays);
        }
        else
        {
            endDate = _timeProvider.GetUtcNow().UtcDateTime;
            days = filter.TimeRange?.ToLowerInvariant() switch
            {
                "7d" => 7,
                "90d" => 90,
                "year" => 365,
                _ => 30
            };
            startDate = endDate.AddDays(-days);
        }

        var previousStartDate = startDate.AddDays(-days);
        var previousEndDate = startDate;

        return (startDate, endDate, previousStartDate, previousEndDate);
    }

    private static MetricValueDto CreateMetric(long current, long previous)
    {
        decimal growth = 0m;
        if (previous > 0)
        {
            growth = Math.Round(((decimal)(current - previous) / previous) * 100m, 1);
        }
        else if (current > 0)
        {
            growth = 100m;
        }

        return new MetricValueDto
        {
            Value = current,
            PreviousValue = previous,
            GrowthPercentage = growth
        };
    }

    private static decimal CalculatePercentage(long part, long total)
    {
        if (total <= 0) return 0m;
        return Math.Round(((decimal)part * 100m) / total, 1);
    }
}
