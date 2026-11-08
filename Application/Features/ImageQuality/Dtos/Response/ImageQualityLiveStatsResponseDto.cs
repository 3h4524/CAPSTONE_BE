namespace APCS.Application.Features.ImageQuality.Dtos.Response;

/// <summary>The design images the jobs saved, grouped by image provider and prompt version.</summary>
public sealed record ImageQualityLiveStatsResponseDto(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ImageQualityLiveVersionResponseDto> Versions);

/// <summary>
/// One image provider and prompt version. Every rate is null when it has nothing to count; the counts behind it are
/// given next to it.
/// </summary>
/// <param name="LabeledImages">Images that carry their prompt version; the rest were placed by date.</param>
/// <param name="ReviewedImages">Images from jobs that asked for a review.</param>
/// <param name="ApprovalRate">Approved images among the reviewed ones that were approved or rejected.</param>
/// <param name="RegenerateRate">Regenerate requests per product that got designs.</param>
/// <param name="FailureRate">Failed or blocked attempts among all attempts (saved images included).</param>
/// <param name="TextCheckedImages">Images whose text was read by the OCR check when they were made.</param>
/// <param name="TextOkImages">Checked images whose text was exactly the phrase asked for, with no other words.</param>
/// <param name="TextOkRate">Correct-text images among the checked ones.</param>
/// <param name="AverageCharacterErrorRate">Mean character error rate of the checked images that print a phrase.</param>
public sealed record ImageQualityLiveVersionResponseDto(
    string Key,
    string Label,
    string Provider,
    DateTimeOffset? SinceUtc,
    DateTimeOffset? UntilUtc,
    int Images,
    int LabeledImages,
    int Products,
    int Jobs,
    int ReviewedImages,
    int ApprovedImages,
    int RejectedImages,
    double? ApprovalRate,
    int RegenerateRequests,
    double? RegenerateRate,
    int FailedAttempts,
    int BlockedAttempts,
    double? FailureRate,
    double? AverageGenerationSeconds,
    decimal? AverageCostUsd,
    decimal TotalCostUsd,
    int TextCheckedImages = 0,
    int TextOkImages = 0,
    double? TextOkRate = null,
    double? AverageCharacterErrorRate = null);
