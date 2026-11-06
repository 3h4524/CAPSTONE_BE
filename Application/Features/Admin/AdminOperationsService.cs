using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ListingContentEntity = APCS.Domain.Entities.ListingContent;

namespace APCS.Application.Features.Admin;

/// <summary>
/// Implements <see cref="IAdminOperationsService"/> over the platform's production records.
/// </summary>
public sealed class AdminOperationsService(
    IRepository<BatchJob> batchJobs,
    IRepository<Product> products,
    IRepository<DesignImage> designImages,
    IRepository<MockupImage> mockupImages,
    IRepository<ListingContentEntity> listingContents,
    IRepository<EtsyUploadLog> etsyUploads,
    IRepository<PrintifyUploadLog> printifyUploads,
    IRepository<ApiUsageRecord> apiUsage,
    TimeProvider timeProvider) : IAdminOperationsService
{
    private const string UploadSucceeded = "success";
    private const string CallSucceeded = "success";

    /// <inheritdoc />
    public async Task<Result<AdminOperationsDto>> GetOperationsAsync(string timeRange, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var (periodStart, bucket) = Period(timeRange, today);
        var from = Utc(periodStart);
        var until = Utc(today.AddDays(1));
        var previousFrom = Utc(periodStart.AddDays(-(today.DayNumber - periodStart.DayNumber + 1)));

        var jobsByDay = await batchJobs.Query()
            .Where(job => job.DeletedAt == null && job.CreatedAt >= from && job.CreatedAt < until)
            .GroupBy(job => job.CreatedAt!.Value.Date)
            .Select(day => new { Day = day.Key, Count = day.Count() })
            .ToListAsync(cancellationToken);

        var createdProducts = products.Query()
            .Where(product => product.DeletedAt == null && product.CreatedAt >= from && product.CreatedAt < until);
        var productsByDay = await createdProducts
            .GroupBy(product => product.CreatedAt!.Value.Date)
            .Select(day => new { Day = day.Key, Count = day.Count() })
            .ToListAsync(cancellationToken);

        var throughput = Buckets(periodStart, today, bucket)
            .Select(start =>
            {
                var end = Next(start, bucket);
                bool InBucket(DateTime day) => DateOnly.FromDateTime(day) >= start && DateOnly.FromDateTime(day) < end;
                return new OperationsBucketDto(
                    start,
                    jobsByDay.Where(day => InBucket(day.Day)).Sum(day => day.Count),
                    productsByDay.Where(day => InBucket(day.Day)).Sum(day => day.Count));
            })
            .ToList();

        var activeSellers = await batchJobs.Query()
            .Where(job => job.DeletedAt == null && job.CreatedAt >= from && job.CreatedAt < until)
            .Select(job => job.UserId).Distinct().CountAsync(cancellationToken);
        var previousActiveSellers = await batchJobs.Query()
            .Where(job => job.DeletedAt == null && job.CreatedAt >= previousFrom && job.CreatedAt < from)
            .Select(job => job.UserId).Distinct().CountAsync(cancellationToken);

        var productIds = createdProducts.Select(product => product.Id);
        var funnel = new ProductionFunnelDto(
            await createdProducts.CountAsync(cancellationToken),
            await designImages.Query()
                .Where(image => image.DeletedAt == null && productIds.Contains(image.ProductId))
                .Select(image => image.ProductId).Distinct().CountAsync(cancellationToken),
            await mockupImages.Query()
                .Where(image => image.DeletedAt == null && productIds.Contains(image.ProductId))
                .Select(image => image.ProductId).Distinct().CountAsync(cancellationToken),
            await listingContents.Query()
                .Where(listing => productIds.Contains(listing.ProductId))
                .Select(listing => listing.ProductId).Distinct().CountAsync(cancellationToken),
            await etsyUploads.Query()
                .Where(upload => upload.UploadStatus == UploadSucceeded && productIds.Contains(upload.ProductId))
                .Select(upload => upload.ProductId)
                .Union(printifyUploads.Query()
                    .Where(upload => upload.UploadStatus == UploadSucceeded && productIds.Contains(upload.ProductId))
                    .Select(upload => upload.ProductId))
                .CountAsync(cancellationToken));

        var aiUsage = (await apiUsage.Query()
                .Where(call => call.CreatedAt >= from && call.CreatedAt < until)
                .GroupBy(call => call.Feature)
                .Select(feature => new AiFeatureUsageDto(
                    feature.Key,
                    feature.Count(),
                    feature.Count(call => call.Status != CallSucceeded),
                    feature.Sum(call => call.CostUsd)))
                .ToListAsync(cancellationToken))
            .OrderByDescending(feature => feature.Calls)
            .ToList();

        return Result.Success(new AdminOperationsDto(
            periodStart, today, bucket, throughput, activeSellers, previousActiveSellers, funnel, aiUsage));
    }

    // The same periods as the Dashboard's revenue trend: 7 or 30 days by day, 6 or 12 months by month.
    private static (DateOnly Start, string Bucket) Period(string timeRange, DateOnly today)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        return timeRange.ToLowerInvariant() switch
        {
            "week" => (today.AddDays(-6), "day"),
            "halfyear" => (thisMonth.AddMonths(-5), "month"),
            "year" => (thisMonth.AddMonths(-11), "month"),
            _ => (today.AddDays(-29), "day"),
        };
    }

    private static IEnumerable<DateOnly> Buckets(DateOnly periodStart, DateOnly today, string bucket)
    {
        for (var current = periodStart; current <= today; current = Next(current, bucket))
        {
            yield return current;
        }
    }

    private static DateOnly Next(DateOnly start, string bucket) => bucket == "day" ? start.AddDays(1) : start.AddMonths(1);

    private static DateTime Utc(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
