using APCS.Application.Features.DesignGeneration.Common;

namespace APCS.Application.Features.DesignGeneration.Dtos.Response;

/// <summary>One generated design image shown on the review grid.</summary>
/// <param name="TextCheck">What OCR read in the design compared with the phrase it should print; null when it was not checked.</param>
public sealed record GeneratedImageDto(
    Guid Id,
    string ImageUrl,
    int VariationIndex,
    int WidthPx,
    int HeightPx,
    string ApprovalStatus,
    DesignTextCheckDto? TextCheck = null);

/// <summary>The OCR check of a design's text, shown to the Seller as a warning, never as a block.</summary>
/// <param name="Ok">Every word of the phrase was read, with no other word and no repeat.</param>
/// <param name="Expected">The phrase the design should print; null when it should print no words.</param>
/// <param name="Read">The text OCR read, line by line.</param>
public sealed record DesignTextCheckDto(
    bool Ok,
    string? Expected,
    string Read,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Extra,
    IReadOnlyList<string> Repeated)
{
    public static DesignTextCheckDto? From(DesignTextCheckResult? result) =>
        result is null ? null : new(result.Ok, result.Expected, result.Read, result.Missing, result.Extra, result.Repeated);
}

/// <summary>Per-product state and result inside a batch job (SRS 3.4.10 table, 3.5.12 grid).</summary>
/// <param name="DesignText">The words the product's designs print, as the product has them now.</param>
/// <param name="CustomPrompt">The product's own image prompt, as it has it now; null when its designs come from the template.</param>
/// <param name="DesignTemplateId">The template the product's designs are made from now.</param>
/// <param name="Instructions">The instructions set for this product in the job; null uses the job's.</param>
public sealed record BatchJobProductResultDto(
    Guid Id,
    Guid? ProductId,
    string ProductName,
    string ProductType,
    int Sequence,
    string Status,
    string? ErrorMessage,
    IReadOnlyList<GeneratedImageDto> Images,
    string? DesignText = null,
    string? CustomPrompt = null,
    Guid? DesignTemplateId = null,
    string? Instructions = null);

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
    IReadOnlyList<BatchJobProductResultDto> Products,
    bool? RequireApproval = null,
    Guid? WorkflowId = null,
    bool TextCheckPending = false);

/// <summary>A row in the job list of one batch.</summary>
/// <param name="WorkflowId">The workflow the job was started from; null when it was started outside one, or before this was recorded.</param>
public sealed record BatchJobSummaryDto(
    Guid Id,
    string Status,
    int TotalProducts,
    int ProcessedProducts,
    int FailedProducts,
    DateTime? CreatedAt,
    DateTime? StartedAt,
    Guid? WorkflowId = null);

/// <summary>How many products went back to pending.</summary>
public sealed record ResetProductsResultDto(int ResetCount);

/// <summary>How the images of a batch job stand after an approval change.</summary>
/// <param name="UpdatedCount">How many images changed status in this call.</param>
public sealed record ImageApprovalResultDto(int UpdatedCount, int Pending, int Approved, int Rejected);
