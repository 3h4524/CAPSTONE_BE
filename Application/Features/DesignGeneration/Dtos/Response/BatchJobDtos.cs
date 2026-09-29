namespace APCS.Application.Features.DesignGeneration.Dtos.Response;

/// <summary>One generated design image shown on the review grid.</summary>
public sealed record GeneratedImageDto(
    Guid Id,
    string ImageUrl,
    int VariationIndex,
    int WidthPx,
    int HeightPx,
    string ApprovalStatus);

/// <summary>Per-product state and result inside a batch job (SRS 3.4.10 table, 3.5.12 grid).</summary>
public sealed record BatchJobProductResultDto(
    Guid Id,
    Guid? ProductId,
    string ProductName,
    int Sequence,
    string Status,
    string? ErrorMessage,
    IReadOnlyList<GeneratedImageDto> Images);

/// <summary>SRS BR45 counters: Pending, Processing, Completed, Failed.</summary>
public sealed record BatchJobCountersDto(int Pending, int Processing, int Completed, int Failed);

/// <summary>Everything the Batch Job Dashboard / review page needs in one call.</summary>
public sealed record BatchJobDetailDto(
    Guid Id,
    Guid BatchId,
    string BatchName,
    string Status,
    int TotalProducts,
    int ProcessedProducts,
    int FailedProducts,
    decimal ProgressPercentage,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int VariationCount,
    string AspectRatio,
    BatchJobCountersDto Counters,
    IReadOnlyList<BatchJobProductResultDto> Products);

/// <summary>A row in the job list of one batch.</summary>
public sealed record BatchJobSummaryDto(
    Guid Id,
    string Status,
    int TotalProducts,
    int ProcessedProducts,
    int FailedProducts,
    DateTime? CreatedAt,
    DateTime? StartedAt);
