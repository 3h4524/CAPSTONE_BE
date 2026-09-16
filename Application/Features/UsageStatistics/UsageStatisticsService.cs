using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.UsageStatistics.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.UsageStatistics;

public sealed class UsageStatisticsService(
    ICurrentUser currentUser,
    IRepository<ApiUsageRecord> records,
    IUsageStatisticRepository statistics,
    ISubscriptionRepository subscriptions,
    TimeProvider timeProvider) : IUsageStatisticsService
{
    public async Task<Result<UsageOverviewResponseDto>> GetOverviewAsync(int days, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<UsageOverviewResponseDto>(Error.Unauthorized("Usage.Unauthenticated", "Please sign in to view usage."));
        if (days is not (30 or 90 or 365))
            days = 30;

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = today.AddDays(-(days - 1));
        var fromDate = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDate = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var usage = await records.Query().Where(x => x.UserId == userId && x.CreatedAt >= fromDate && x.CreatedAt < toDate).ToListAsync(cancellationToken);
        var daily = Enumerable.Range(0, days).Select(offset =>
        {
            var date = from.AddDays(offset);
            var day = usage.Where(x => x.CreatedAt.HasValue && DateOnly.FromDateTime(x.CreatedAt.Value) == date).ToList();
            return new UsageDayDto(date, Count(day, "image"), Count(day, "video"));
        }).ToArray();
        var images = daily.Sum(x => x.Images);
        var videos = daily.Sum(x => x.Videos);
        var listings = Count(usage, "listing");
        var cost = usage.Sum(x => x.CostUsd);
        var grouped = usage.GroupBy(x => new { x.Provider, Service = ServiceName(x.Feature) }).Select(group => new { group.Key.Provider, group.Key.Service, Requests = group.Sum(x => x.RequestUnits), Cost = group.Sum(x => x.CostUsd) }).ToList();
        var total = grouped.Sum(x => x.Cost);
        var costs = grouped.OrderByDescending(x => x.Cost).Select(x => new UsageCostRowDto(x.Provider, x.Service, x.Requests, x.Cost, total == 0 ? 0 : Math.Round(x.Cost / total * 100, 2))).ToArray();
        var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        var period = await statistics.GetCurrentPeriodAsync(userId, today, cancellationToken);
        var limit = subscription?.Plan.ApiCallQuota ?? 0;
        var used = period?.TotalApiCalls ?? usage.Sum(x => x.RequestUnits);
        var reset = subscription?.RenewalDate ?? today.AddMonths(1);
        var allowance = new UsageAllowanceDto(subscription?.Plan.Name ?? "No active plan", used, limit, limit == 0 ? 0 : Math.Round((decimal)used / limit * 100, 1), reset, Math.Max(0, reset.DayNumber - today.DayNumber), limit > 0 && used > limit);
        return Result.Success(new UsageOverviewResponseDto(new UsageSummaryDto(images, videos, listings, cost), daily, allowance, costs, usage.Count > 0));
    }

    private static int Count(IEnumerable<ApiUsageRecord> source, string term) => source.Count(x => x.Feature.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string ServiceName(string feature) => feature.Contains("image", StringComparison.OrdinalIgnoreCase) ? "Image generation" : feature.Contains("video", StringComparison.OrdinalIgnoreCase) ? "Video rendering" : feature.Contains("list", StringComparison.OrdinalIgnoreCase) ? "Listing content" : feature;
}
