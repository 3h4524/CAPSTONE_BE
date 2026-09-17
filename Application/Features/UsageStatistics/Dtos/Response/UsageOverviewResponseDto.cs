namespace APCS.Application.Features.UsageStatistics.Dtos.Response;

public sealed record UsageOverviewResponseDto(
    UsageSummaryDto Summary,
    IReadOnlyList<UsageDayDto> DailyActivity,
    UsageAllowanceDto Allowance,
    IReadOnlyList<UsageCostRowDto> Costs,
    bool HasData);

public sealed record UsageSummaryDto(int Images, int Videos, int Listings, decimal EstimatedCostUsd);
public sealed record UsageDayDto(DateOnly Date, int Images, int Videos);
public sealed record UsageAllowanceDto(string PlanName, int Used, int Limit, decimal Percent, DateOnly ResetDate, int DaysRemaining, bool AtRisk);
public sealed record UsageCostRowDto(string Provider, string Service, int Requests, decimal EstimatedCostUsd, decimal SharePercent);
