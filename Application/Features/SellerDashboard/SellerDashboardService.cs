using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.SellerDashboard.Common;
using APCS.Application.Features.SellerDashboard.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ListingContentEntity = APCS.Domain.Entities.ListingContent;

namespace APCS.Application.Features.SellerDashboard;

/// <summary>
/// Implements <see cref="ISellerDashboardService"/> over the Seller's production records.
/// </summary>
public sealed class SellerDashboardService(
    ICurrentUser currentUser,
    IRepository<Product> products,
    IRepository<Batch> batches,
    IRepository<BatchJob> batchJobs,
    IRepository<BatchJobProduct> batchJobProducts,
    IRepository<PromoVideo> promoVideos,
    IRepository<Workflow> workflows,
    IRepository<WorkflowRun> workflowRuns,
    IRepository<ListingContentEntity> listingContents,
    IRepository<SeoScore> seoScores,
    IRepository<EtsyIntegration> etsyIntegrations,
    IRepository<EtsyUploadLog> etsyUploads,
    IRepository<ApiUsageRecord> apiUsage,
    TimeProvider timeProvider) : ISellerDashboardService
{
    private const string UploadSucceeded = "success";
    private const string UploadFailed = "failed";
    private const string Failed = "failed";
    private const string CallSucceeded = "success";
    private const string PendingApproval = "pending";
    private const string VideoCompleted = "completed";
    private const int ScalingPublishedListings = 20;
    private const int LowSeoScore = 60;
    private const int RecentFailureDays = 14;
    private const int DurationSampleSize = 200;
    private const int RecentJobCount = 5;
    private const int MaxTargets = 3;

    private static readonly string[] ActiveJobStatuses = ["validating", "queued", "running", "paused"];
    private static readonly string[] FailedJobStatuses = ["failed", "partially_completed"];
    private static readonly string[] DesignReviewStatuses = ["image_review_required"];

    /// <summary>A product that has been processing too long, with the batch job it is in.</summary>
    private sealed record StuckProduct(string Stage, Guid JobId, string JobName, string? JobConfig);

    /// <inheritdoc />
    public async Task<Result<DashboardSummaryResponseDto>> GetSummaryAsync(int days, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return Result.Failure<DashboardSummaryResponseDto>(Unauthenticated());
        if (days is not (7 or 30 or 90))
            days = 30;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var periodStart = today.AddDays(-(days - 1));
        var from = Utc(periodStart);
        var until = Utc(today.AddDays(1));
        var previousFrom = from.AddDays(-days);

        var ownProducts = products.Query().Where(product => product.UserId == userId && product.DeletedAt == null);
        var ownJobProducts = batchJobProducts.Query().Where(item => item.BatchJob.UserId == userId);
        var ownUploads = etsyUploads.Query().Where(upload => upload.EtsyIntegration.UserId == userId);
        var ownCalls = apiUsage.Query().Where(call => call.UserId == userId);

        async Task<int> ProductsCreated(DateTime start, DateTime end) =>
            await ownProducts.CountAsync(product => product.CreatedAt >= start && product.CreatedAt < end, cancellationToken);

        async Task<decimal> SuccessRate(DateTime start, DateTime end)
        {
            var finished = ownJobProducts.Where(item => item.CompletedAt >= start && item.CompletedAt < end);
            var total = await finished.CountAsync(cancellationToken);
            var failed = await finished.CountAsync(item => item.Status == Failed, cancellationToken);
            return total == 0 ? 0 : Math.Round((decimal)(total - failed) / total * 100, 1);
        }

        async Task<int> Published(DateTime start, DateTime end) =>
            await ownUploads.CountAsync(upload => upload.UploadStatus == UploadSucceeded && upload.CompletedAt >= start && upload.CompletedAt < end, cancellationToken);

        async Task<decimal> CostPerProduct(DateTime start, DateTime end, int created)
        {
            var cost = await ownCalls
                .Where(call => call.CreatedAt >= start && call.CreatedAt < end)
                .SumAsync(call => call.CostUsd, cancellationToken);
            return created == 0 ? 0 : Math.Round(cost / created, 4);
        }

        var created = await ProductsCreated(from, until);
        var previousCreated = await ProductsCreated(previousFrom, from);
        var kpis = new DashboardKpisDto(
            Kpi(created, previousCreated),
            Kpi(await SuccessRate(from, until), await SuccessRate(previousFrom, from)),
            Kpi(await Published(from, until), await Published(previousFrom, from)),
            Kpi(await CostPerProduct(from, until, created), await CostPerProduct(previousFrom, from, previousCreated)));

        var trend = await TrendAsync(ownProducts, ownJobProducts, ownCalls, periodStart, today, from, until, cancellationToken);

        var statusCounts = await ownProducts
            .GroupBy(product => product.ProcessingStatus)
            .Select(status => new { Status = status.Key, Count = status.Count() })
            .ToListAsync(cancellationToken);
        var stuck = await StuckProductsAsync(userId, now, cancellationToken);
        var pipeline = PipelineStages.Ordered
            .Select(stage => new PipelineStageDto(
                stage,
                statusCounts.Where(status => PipelineStages.StageOf(status.Status) == stage).Sum(status => status.Count),
                stuck.Count(item => item.Stage == stage)))
            .ToList();
        var failedProducts = statusCounts.Where(status => status.Status == Failed).Sum(status => status.Count);

        var activeBatches = (await batchJobs.Query()
                .Where(job => job.UserId == userId && job.DeletedAt == null && ActiveJobStatuses.Contains(job.Status))
                .OrderByDescending(job => job.CreatedAt)
                .Select(job => new
                {
                    job.Id, job.Name, job.Status, job.ProgressPercentage, job.ProcessedProducts,
                    job.FailedProducts, job.TotalProducts, job.EstimatedCompletionTime, job.Config,
                })
                .ToListAsync(cancellationToken))
            .Select(job => new ActiveBatchDto(
                job.Id, job.Name, job.Status, job.ProgressPercentage ?? 0, job.ProcessedProducts ?? 0,
                job.FailedProducts ?? 0, job.TotalProducts ?? 0, job.EstimatedCompletionTime,
                DashboardLinks.BatchJob(job.Id, job.Config)))
            .ToList();

        var recentJobs = (await batchJobs.Query()
                .Where(job => job.UserId == userId && job.DeletedAt == null)
                .OrderByDescending(job => job.CreatedAt)
                .Take(RecentJobCount)
                .Select(job => new { job.Id, job.Name, job.Status, job.TotalProducts, job.FailedProducts, job.CreatedAt, job.Config })
                .ToListAsync(cancellationToken))
            .Select(job => new RecentJobDto(
                job.Id, job.Name, job.Status, job.TotalProducts ?? 0, job.FailedProducts ?? 0, job.CreatedAt,
                DashboardLinks.BatchJob(job.Id, job.Config)))
            .ToList();

        var aiUsage = (await ownCalls
                .Where(call => call.CreatedAt >= from && call.CreatedAt < until)
                .GroupBy(call => call.Feature)
                .Select(feature => new AiFeatureCostDto(
                    feature.Key,
                    feature.Count(),
                    feature.Count(call => call.Status != CallSucceeded),
                    feature.Sum(call => call.CostUsd)))
                .ToListAsync(cancellationToken))
            .OrderByDescending(feature => feature.CostUsd)
            .ToList();

        var insights = DashboardInsights.Build(
            kpis,
            await NicheOutcomesAsync(ownProducts, from, until, cancellationToken),
            await TopFailureAsync(ownJobProducts, from, until, cancellationToken));

        var stage = await StageAsync(ownProducts, ownUploads, activeBatches.Count, cancellationToken);

        return Result.Success(new DashboardSummaryResponseDto(
            stage, days, kpis, trend, pipeline, failedProducts, activeBatches, recentJobs, aiUsage, insights));
    }

    /// <inheritdoc />
    public async Task<Result<DashboardActionItemsResponseDto>> GetActionItemsAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId)
            return Result.Failure<DashboardActionItemsResponseDto>(Unauthenticated());

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var items = new List<ActionItemDto>();
        var deletedBatches = await DeletedBatchesAsync(userId, cancellationToken);

        void Add(string type, string severity, int count, DateTime? oldestAt, IEnumerable<ActionTargetDto> targets, string fallbackUrl)
        {
            if (count == 0)
                return;
            var top = targets.OrderByDescending(target => target.Count).ThenBy(target => target.Label).ToList();
            // One place holds all of it: go straight there. Several: the list, with the largest shown under the item.
            var actionUrl = top.Count == 1 ? top[0].Url : fallbackUrl;
            items.Add(new ActionItemDto(type, severity, count, ActionItemPriority.Calculate(severity, count, oldestAt, now),
                oldestAt, actionUrl, top.Take(MaxTargets).ToList()));
        }

        // An expired Etsy connection blocks every upload at once, so it is one item however many listings wait.
        var expired = await etsyIntegrations.Query()
            .Where(integration => integration.UserId == userId && integration.DeletedAt == null
                && integration.IsActive != false && integration.OauthExpiresAt < now)
            .Select(integration => new { integration.ShopName, integration.OauthExpiresAt })
            .ToListAsync(cancellationToken);
        Add(ActionItemTypes.EtsyReconnect, ActionItemSeverities.Critical, expired.Count, expired.Min(shop => shop.OauthExpiresAt),
            expired.Select(shop => new ActionTargetDto(shop.ShopName, 1, "/shop")), "/shop");

        // A product counts once it failed and has not been uploaded since. An upload before the
        // failure does not clear it.
        var allUploads = etsyUploads.Query();
        var failedUploads = await allUploads
            .Where(upload => upload.EtsyIntegration.UserId == userId && upload.UploadStatus == UploadFailed
                && upload.Product.DeletedAt == null && !deletedBatches.Contains(upload.Product.BatchId)
                && !allUploads.Any(later => later.ProductId == upload.ProductId && later.UploadStatus == UploadSucceeded
                    && later.CreatedAt > upload.CreatedAt))
            .GroupBy(upload => new { upload.ProductId, upload.Product.BatchId })
            .Select(product => new { product.Key.BatchId, At = product.Min(upload => upload.CreatedAt) })
            .ToListAsync(cancellationToken);
        var uploadBatchNames = await BatchNamesAsync(failedUploads.Select(upload => upload.BatchId), cancellationToken);
        Add(ActionItemTypes.EtsyUploadFailed, ActionItemSeverities.Error, failedUploads.Count, failedUploads.Min(upload => upload.At),
            failedUploads.GroupBy(upload => upload.BatchId).Select(batch => new ActionTargetDto(
                uploadBatchNames.GetValueOrDefault(batch.Key, "Batch"), batch.Count(), DashboardLinks.Batch(batch.Key, DashboardLinks.BatchFilters.Completed))),
            "/batches");

        var recentFailure = now.AddDays(-RecentFailureDays);
        var failedJobs = await batchJobs.Query()
            .Where(job => job.UserId == userId && job.DeletedAt == null && !deletedBatches.Contains(job.BatchId)
                && FailedJobStatuses.Contains(job.Status)
                && (job.CompletedAt ?? job.UpdatedAt) >= recentFailure)
            .Select(job => new { job.Id, job.Name, job.Config, job.FailedProducts, At = job.CompletedAt ?? job.UpdatedAt })
            .ToListAsync(cancellationToken);
        Add(ActionItemTypes.BatchFailed, ActionItemSeverities.Error, failedJobs.Count, failedJobs.Min(job => job.At),
            failedJobs.Select(job => new ActionTargetDto(job.Name, job.FailedProducts ?? 0, DashboardLinks.BatchJob(job.Id, job.Config))),
            "/batches");

        // A failed render that a later render of the same product replaced is history, not work.
        var ownVideos = promoVideos.Query().Where(video => video.Product.UserId == userId && video.DeletedAt == null
            && video.Product.DeletedAt == null && !deletedBatches.Contains(video.Product.BatchId));
        var failedVideos = await VideoRowsAsync(ownVideos
            .Where(video => video.Status == Failed
                && !ownVideos.Any(other => other.ProductId == video.ProductId && other.Status == VideoCompleted
                    && other.CreatedAt > video.CreatedAt)), cancellationToken);
        Add(ActionItemTypes.VideoFailed, ActionItemSeverities.Error, failedVideos.Count, failedVideos.Min(video => video.At),
            await WorkflowTargetsAsync(failedVideos, null, cancellationToken), "/workflows");

        var stuck = await StuckProductsAsync(userId, now, cancellationToken);
        Add(ActionItemTypes.StuckProducts, ActionItemSeverities.Warning, stuck.Count, null,
            stuck.GroupBy(item => item.JobId).Select(job => new ActionTargetDto(
                job.First().JobName, job.Count(), DashboardLinks.BatchJob(job.Key, job.First().JobConfig))),
            "/batches");

        var designReviews = await batchJobProducts.Query()
            .Where(item => item.BatchJob.UserId == userId && item.BatchJob.DeletedAt == null
                && !deletedBatches.Contains(item.BatchJob.BatchId)
                && (item.Product == null || item.Product.DeletedAt == null)
                && DesignReviewStatuses.Contains(item.Status))
            .Select(item => new { item.BatchJobId, JobName = item.BatchJob.Name, JobConfig = item.BatchJob.Config, At = item.CompletedAt ?? item.UpdatedAt })
            .ToListAsync(cancellationToken);
        Add(ActionItemTypes.DesignReview, ActionItemSeverities.Warning, designReviews.Count, designReviews.Min(item => item.At),
            designReviews.GroupBy(item => item.BatchJobId).Select(job => new ActionTargetDto(
                job.First().JobName, job.Count(), DashboardLinks.BatchJob(job.Key, job.First().JobConfig))),
            "/batches");

        // A listing and its SEO score are reviewed on the Generate Listing step of the product's workflow run.
        var listingReviews = await WithLatestRunAsync(await listingContents.Query()
            .Where(listing => listing.Product.UserId == userId && listing.Product.DeletedAt == null
                && !deletedBatches.Contains(listing.Product.BatchId) && listing.ApprovalStatus == PendingApproval)
            .Select(listing => new ProductMoment(listing.ProductId, listing.Product.BatchId, listing.CreatedAt))
            .ToListAsync(cancellationToken), cancellationToken);
        Add(ActionItemTypes.ListingReview, ActionItemSeverities.Warning, listingReviews.Count, listingReviews.Min(listing => listing.At),
            await WorkflowTargetsAsync(listingReviews, DashboardLinks.BatchFilters.Completed, cancellationToken), "/workflows");

        var videoReviews = await VideoRowsAsync(ownVideos
            .Where(video => video.Status == VideoCompleted && video.ApprovalStatus == PendingApproval), cancellationToken);
        Add(ActionItemTypes.VideoReview, ActionItemSeverities.Warning, videoReviews.Count, videoReviews.Min(video => video.At),
            await WorkflowTargetsAsync(videoReviews, null, cancellationToken), "/workflows");

        var lowSeo = await WithLatestRunAsync(await seoScores.Query()
            .Where(score => score.ListingContent.Product.UserId == userId && score.ListingContent.Product.DeletedAt == null
                && !deletedBatches.Contains(score.ListingContent.Product.BatchId)
                && score.OverallSeoScore < LowSeoScore)
            .Select(score => new ProductMoment(score.ListingContent.ProductId, score.ListingContent.Product.BatchId, score.CalculatedAt))
            .ToListAsync(cancellationToken), cancellationToken);
        Add(ActionItemTypes.LowSeo, ActionItemSeverities.Info, lowSeo.Count, lowSeo.Min(score => score.At),
            await WorkflowTargetsAsync(lowSeo, DashboardLinks.BatchFilters.Completed, cancellationToken), "/workflows");

        return Result.Success(new DashboardActionItemsResponseDto(items
            .OrderByDescending(item => ActionItemPriority.Weight(item.Severity))
            .ThenByDescending(item => item.Priority)
            .ToList()));
    }

    /// <summary>One point per day of the period: products added, products finished and failed, AI spend.</summary>
    private static async Task<List<TrendPointDto>> TrendAsync(
        IQueryable<Product> ownProducts,
        IQueryable<BatchJobProduct> ownJobProducts,
        IQueryable<ApiUsageRecord> ownCalls,
        DateOnly periodStart,
        DateOnly today,
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
    {
        var created = await ownProducts
            .Where(product => product.CreatedAt >= from && product.CreatedAt < until)
            .GroupBy(product => product.CreatedAt!.Value.Date)
            .Select(day => new { Day = day.Key, Count = day.Count() })
            .ToListAsync(cancellationToken);
        var finished = await ownJobProducts
            .Where(item => item.CompletedAt >= from && item.CompletedAt < until)
            .GroupBy(item => item.CompletedAt!.Value.Date)
            .Select(day => new { Day = day.Key, Count = day.Count(), Failed = day.Count(item => item.Status == Failed) })
            .ToListAsync(cancellationToken);
        var cost = await ownCalls
            .Where(call => call.CreatedAt >= from && call.CreatedAt < until)
            .GroupBy(call => call.CreatedAt!.Value.Date)
            .Select(day => new { Day = day.Key, Cost = day.Sum(call => call.CostUsd) })
            .ToListAsync(cancellationToken);

        var points = new List<TrendPointDto>();
        for (var day = periodStart; day <= today; day = day.AddDays(1))
        {
            var date = day;
            bool Same(DateTime value) => DateOnly.FromDateTime(value) == date;
            var done = finished.FirstOrDefault(row => Same(row.Day));
            points.Add(new TrendPointDto(
                date,
                created.Where(row => Same(row.Day)).Sum(row => row.Count),
                done?.Count ?? 0,
                done?.Failed ?? 0,
                cost.Where(row => Same(row.Day)).Sum(row => row.Cost)));
        }

        return points;
    }

    /// <summary>The period's products by niche, with how many failed, for <see cref="DashboardInsights"/>.</summary>
    private static async Task<List<NicheOutcome>> NicheOutcomesAsync(
        IQueryable<Product> ownProducts, DateTime from, DateTime until, CancellationToken cancellationToken) =>
        (await ownProducts
            .Where(product => product.CreatedAt >= from && product.CreatedAt < until && product.NicheCategory != null)
            .GroupBy(product => product.NicheCategory!)
            .Select(niche => new { Niche = niche.Key, Total = niche.Count(), Failed = niche.Count(product => product.ProcessingStatus == Failed) })
            .ToListAsync(cancellationToken))
        .Select(niche => new NicheOutcome(niche.Niche, niche.Total, niche.Failed))
        .ToList();

    /// <summary>The error the period's failed products stopped on most often, how often, and of how many failures.</summary>
    private static async Task<(string Message, int Count, int TotalFailed)?> TopFailureAsync(
        IQueryable<BatchJobProduct> ownJobProducts, DateTime from, DateTime until, CancellationToken cancellationToken)
    {
        var failures = await ownJobProducts
            .Where(item => item.Status == Failed && item.CompletedAt >= from && item.CompletedAt < until)
            .GroupBy(item => item.ErrorMessage)
            .Select(reason => new { Message = reason.Key, Count = reason.Count() })
            .ToListAsync(cancellationToken);
        var top = failures.Where(reason => !string.IsNullOrWhiteSpace(reason.Message)).MaxBy(reason => reason.Count);
        return top is null ? null : (top.Message!, top.Count, failures.Sum(reason => reason.Count));
    }

    /// <summary>A product waiting on the Seller, and when it started waiting.</summary>
    private sealed record ProductMoment(Guid ProductId, Guid BatchId, DateTime? At);

    /// <summary>A product waiting on the Seller, with the workflow run where it is dealt with, if it has one.</summary>
    private sealed record ProductRow(Guid ProductId, Guid BatchId, Guid? RunId, Guid? WorkflowId, DateTime? At);

    /// <summary>Each product with its latest workflow run, which is where its listing and SEO are reviewed.</summary>
    private async Task<List<ProductRow>> WithLatestRunAsync(List<ProductMoment> products, CancellationToken cancellationToken)
    {
        var productIds = products.Select(product => product.ProductId).Distinct().ToList();
        var latestRuns = (await workflowRuns.Query()
                .Where(run => productIds.Contains(run.ProductId))
                .Select(run => new { run.ProductId, run.Id, run.WorkflowId, run.CreatedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(run => run.ProductId)
            .ToDictionary(runs => runs.Key, runs => runs.OrderByDescending(run => run.CreatedAt).First());

        return products
            .Select(product => latestRuns.TryGetValue(product.ProductId, out var run)
                ? new ProductRow(product.ProductId, product.BatchId, run.Id, run.WorkflowId, product.At)
                : new ProductRow(product.ProductId, product.BatchId, null, null, product.At))
            .ToList();
    }

    /// <summary>One row per product, its latest video's run and workflow, so a product rendered twice counts once.</summary>
    private static async Task<List<ProductRow>> VideoRowsAsync(IQueryable<PromoVideo> videos, CancellationToken cancellationToken) =>
        (await videos
            .Select(video => new
            {
                video.ProductId,
                video.Product.BatchId,
                video.WorkflowRunId,
                WorkflowId = video.WorkflowRunId == null ? (Guid?)null : video.WorkflowRun!.WorkflowId,
                video.CreatedAt,
            })
            .ToListAsync(cancellationToken))
        .GroupBy(video => video.ProductId)
        .Select(product =>
        {
            var latest = product.OrderByDescending(video => video.CreatedAt).First();
            return new ProductRow(product.Key, latest.BatchId, latest.WorkflowRunId, latest.WorkflowId, product.Min(video => video.CreatedAt));
        })
        .ToList();

    /// <summary>
    /// Products by the workflow they were run in: the exact run when one product is waiting there, the
    /// workflow otherwise. Products never run in a workflow go to their batch, filtered when given.
    /// </summary>
    private async Task<List<ActionTargetDto>> WorkflowTargetsAsync(List<ProductRow> rows, string? batchFilter, CancellationToken cancellationToken)
    {
        var workflowIds = rows.Where(row => row.WorkflowId != null).Select(row => row.WorkflowId!.Value).Distinct().ToList();
        var workflowNames = await workflows.Query()
            .Where(workflow => workflowIds.Contains(workflow.Id))
            .ToDictionaryAsync(workflow => workflow.Id, workflow => workflow.Name, cancellationToken);

        var inWorkflows = rows.Where(row => row.WorkflowId != null)
            .GroupBy(row => row.WorkflowId!.Value)
            .Select(workflow => new ActionTargetDto(
                workflowNames.GetValueOrDefault(workflow.Key, "Workflow"),
                workflow.Count(),
                workflow.Count() == 1 && workflow.First().RunId is Guid runId
                    ? DashboardLinks.WorkflowRun(workflow.Key, runId)
                    : DashboardLinks.Workflow(workflow.Key)));
        var inBatches = await BatchTargetsAsync(rows.Where(row => row.WorkflowId == null).Select(row => row.BatchId), batchFilter, cancellationToken);

        return inWorkflows.Concat(inBatches).ToList();
    }

    /// <summary>Counts per batch, each linked to the batch on the Batches page.</summary>
    private async Task<List<ActionTargetDto>> BatchTargetsAsync(IEnumerable<Guid> batchIds, string? filter, CancellationToken cancellationToken)
    {
        var ids = batchIds.ToList();
        var names = await BatchNamesAsync(ids, cancellationToken);
        return ids.GroupBy(id => id)
            .Select(batch => new ActionTargetDto(names.GetValueOrDefault(batch.Key, "Batch"), batch.Count(), DashboardLinks.Batch(batch.Key, filter)))
            .ToList();
    }

    // Deleting a batch marks only the batch, so what it holds has to be left out through it.
    private Task<List<Guid>> DeletedBatchesAsync(Guid userId, CancellationToken cancellationToken) =>
        batches.Query()
            .Where(batch => batch.UserId == userId && batch.DeletedAt != null)
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, string>> BatchNamesAsync(IEnumerable<Guid> batchIds, CancellationToken cancellationToken)
    {
        var ids = batchIds.Distinct().ToList();
        return ids.Count == 0
            ? []
            : await batches.Query()
                .Where(batch => ids.Contains(batch.Id))
                .ToDictionaryAsync(batch => batch.Id, batch => batch.Name, cancellationToken);
    }

    /// <summary>Each product that has been processing longer than <see cref="StuckProductRule"/> allows, with its batch job.</summary>
    private async Task<List<StuckProduct>> StuckProductsAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var deletedBatches = await DeletedBatchesAsync(userId, cancellationToken);
        var ownJobProducts = batchJobProducts.Query().Where(item => item.BatchJob.UserId == userId
            && item.BatchJob.DeletedAt == null && !deletedBatches.Contains(item.BatchJob.BatchId));

        // duration_seconds is not written by the workers, so the average comes from the start and end
        // times. A failure can end in seconds, which says nothing about how long the work takes.
        var recent = await ownJobProducts
            .Where(item => item.StartedAt != null && item.CompletedAt != null && item.Status != Failed)
            .OrderByDescending(item => item.CompletedAt)
            .Take(DurationSampleSize)
            .Select(item => new { Started = item.StartedAt!.Value, Completed = item.CompletedAt!.Value })
            .ToListAsync(cancellationToken);
        decimal? average = recent.Count == 0 ? null : (decimal)recent.Average(item => (item.Completed - item.Started).TotalSeconds);
        var threshold = StuckProductRule.Threshold(average);

        var running = await ownJobProducts
            .Where(item => item.StartedAt != null && item.CompletedAt == null && item.Product != null
                && item.Product.DeletedAt == null
                && PipelineStages.ProcessingStatuses.Contains(item.Product.ProcessingStatus))
            .Select(item => new
            {
                Started = item.StartedAt!.Value,
                item.Product!.ProcessingStatus,
                item.BatchJobId,
                JobName = item.BatchJob.Name,
                JobConfig = item.BatchJob.Config,
            })
            .ToListAsync(cancellationToken);

        return running
            .Where(item => StuckProductRule.IsStuck(item.Started, now, threshold))
            .Select(item => new StuckProduct(PipelineStages.StageOf(item.ProcessingStatus)!, item.BatchJobId, item.JobName, item.JobConfig))
            .ToList();
    }

    private static async Task<string> StageAsync(
        IQueryable<Product> ownProducts,
        IQueryable<EtsyUploadLog> ownUploads,
        int activeBatches,
        CancellationToken cancellationToken)
    {
        if (!await ownProducts.AnyAsync(cancellationToken))
            return "new";
        if (activeBatches > 0)
            return "producing";

        var published = await ownUploads
            .Where(upload => upload.UploadStatus == UploadSucceeded)
            .Select(upload => upload.ProductId).Distinct().CountAsync(cancellationToken);
        return published >= ScalingPublishedListings ? "scaling" : "producing";
    }

    private static KpiDto Kpi(decimal value, decimal previous) =>
        new(value, previous, previous == 0 ? null : Math.Round((value - previous) / previous * 100, 1));

    private static DateTime Utc(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static Error Unauthenticated() => Error.Unauthorized("Dashboard.Unauthenticated", "Please sign in to view your dashboard.");
}
