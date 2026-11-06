namespace APCS.Application.Features.Admin.Dtos.Response;

/// <summary>
/// How the platform's production went over the Dashboard's period: how much was made, how far
/// products got through the pipeline, and what the AI work cost.
/// </summary>
/// <param name="PeriodStart">The first day counted.</param>
/// <param name="PeriodEnd">The last day counted (today).</param>
/// <param name="Bucket">How <paramref name="Throughput"/> is grouped: "day" or "month".</param>
/// <param name="Throughput">Work started in each bucket of the period, empty buckets included.</param>
/// <param name="ActiveSellers">Sellers who started at least one batch job in the period.</param>
/// <param name="PreviousActiveSellers">The same count for the period just before, as long.</param>
/// <param name="Funnel">How far the products created in the period got.</param>
/// <param name="AiUsage">AI calls made in the period, by feature, most used first.</param>
public sealed record AdminOperationsDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Bucket,
    List<OperationsBucketDto> Throughput,
    int ActiveSellers,
    int PreviousActiveSellers,
    ProductionFunnelDto Funnel,
    List<AiFeatureUsageDto> AiUsage);

/// <param name="Start">The bucket's first day.</param>
/// <param name="Jobs">Batch jobs started in it.</param>
/// <param name="Products">Products created in it.</param>
public sealed record OperationsBucketDto(DateOnly Start, int Jobs, int Products);

/// <summary>Products created in the period, and how many of them reached each step.</summary>
/// <param name="Products">Products created.</param>
/// <param name="Designed">Of them, with at least one design.</param>
/// <param name="MockedUp">Of them, with at least one mock-up.</param>
/// <param name="Listed">Of them, with listing content.</param>
/// <param name="Published">Of them, uploaded to Etsy or Printify successfully.</param>
public sealed record ProductionFunnelDto(int Products, int Designed, int MockedUp, int Listed, int Published);

/// <param name="Feature">The AI feature, e.g. "image_generation".</param>
/// <param name="Calls">Calls made.</param>
/// <param name="Failed">Calls that did not succeed.</param>
/// <param name="CostUsd">What the calls cost, in USD.</param>
public sealed record AiFeatureUsageDto(string Feature, int Calls, int Failed, decimal CostUsd);
