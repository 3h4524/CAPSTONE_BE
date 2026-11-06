namespace APCS.Application.Features.SellerDashboard.Dtos.Response;

/// <summary>
/// What a Seller's Dashboard shows: where the Seller stands, the period's figures and how they moved
/// day by day, where their products are in the pipeline, which batch jobs are running or ran last,
/// what the AI work cost, and what in all that is worth pointing out.
/// </summary>
/// <param name="Stage">"new" (nothing made yet), "producing" or "scaling"; the page orders its panels by it.</param>
/// <param name="PeriodDays">The period the figures cover: 7, 30 or 90 days up to today.</param>
/// <param name="Kpis">The period's figures, each beside the period before it.</param>
/// <param name="Trend">One point per day of the period, oldest first, empty days included.</param>
/// <param name="Pipeline">How many of the Seller's products sit at each stage, in pipeline order.</param>
/// <param name="FailedProducts">The Seller's products whose processing failed.</param>
/// <param name="ActiveBatches">The batch jobs that are queued, running or paused, newest first.</param>
/// <param name="RecentJobs">The Seller's latest batch jobs, newest first, whatever their state.</param>
/// <param name="AiUsage">The period's AI calls by feature, the most expensive first.</param>
/// <param name="Insights">What the figures say, the warnings first, see <see cref="Common.DashboardInsights"/>.</param>
public sealed record DashboardSummaryResponseDto(
    string Stage,
    int PeriodDays,
    DashboardKpisDto Kpis,
    IReadOnlyList<TrendPointDto> Trend,
    IReadOnlyList<PipelineStageDto> Pipeline,
    int FailedProducts,
    IReadOnlyList<ActiveBatchDto> ActiveBatches,
    IReadOnlyList<RecentJobDto> RecentJobs,
    IReadOnlyList<AiFeatureCostDto> AiUsage,
    IReadOnlyList<InsightDto> Insights);

/// <summary>One day of the period.</summary>
/// <param name="Products">Products the Seller added that day.</param>
/// <param name="Finished">Products a batch job finished that day, failed ones included.</param>
/// <param name="Failed">Of those, the ones that failed.</param>
/// <param name="CostUsd">What the day's AI calls cost.</param>
public sealed record TrendPointDto(DateOnly Date, int Products, int Finished, int Failed, decimal CostUsd);

/// <summary>A batch job of the Seller, with the exact page to open it on.</summary>
public sealed record RecentJobDto(
    Guid Id,
    string Name,
    string Status,
    int Total,
    int Failed,
    DateTime? CreatedAt,
    string Url);

/// <summary>What one AI feature cost over the period and how often it failed.</summary>
/// <param name="Feature">"image_generation", "mockup", "video", "listing", "seo" or "integration".</param>
public sealed record AiFeatureCostDto(string Feature, int Calls, int Failed, decimal CostUsd);

/// <summary>One thing the figures say, in words.</summary>
/// <param name="Tone">"warning" for something going wrong, "good" for something worth doing more of.</param>
public sealed record InsightDto(string Tone, string Title, string Detail);

/// <summary>A figure for the period and for the period of the same length before it.</summary>
/// <param name="Value">The figure for the period.</param>
/// <param name="PreviousValue">The figure for the period before it.</param>
/// <param name="ChangePercent">How much it moved, in percent; null when the period before had nothing to compare with.</param>
public sealed record KpiDto(decimal Value, decimal PreviousValue, decimal? ChangePercent);

/// <summary>The Dashboard's headline figures.</summary>
/// <param name="ProductsCreated">Products the Seller added.</param>
/// <param name="SuccessRatePercent">Of the products the batch jobs finished, the share that did not fail.</param>
/// <param name="ListingsPublished">Listings uploaded to Etsy successfully.</param>
/// <param name="CostPerProductUsd">AI spend divided by the products added.</param>
public sealed record DashboardKpisDto(
    KpiDto ProductsCreated,
    KpiDto SuccessRatePercent,
    KpiDto ListingsPublished,
    KpiDto CostPerProductUsd);

/// <summary>One column of the pipeline funnel.</summary>
/// <param name="Stage">The stage key, see <see cref="Common.PipelineStages"/>.</param>
/// <param name="Count">The products at this stage.</param>
/// <param name="StuckCount">Of those, the ones that have been processing far longer than usual.</param>
public sealed record PipelineStageDto(string Stage, int Count, int StuckCount);

/// <summary>A batch job that has not finished.</summary>
public sealed record ActiveBatchDto(
    Guid Id,
    string Name,
    string Status,
    decimal ProgressPercent,
    int Processed,
    int Failed,
    int Total,
    DateTime? EstimatedCompletionAt,
    string Url);
