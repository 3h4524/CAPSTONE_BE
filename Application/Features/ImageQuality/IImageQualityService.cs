using APCS.Application.Features.ImageQuality.Dtos.Request;
using APCS.Application.Features.ImageQuality.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.ImageQuality;

/// <summary>
/// What the admin Image Quality page reads: offline scoring runs an admin uploads, and live stats of the design
/// images the jobs saved, both split by the prompt version that made the images.
/// </summary>
public interface IImageQualityService
{
    /// <summary>Lists the stored scoring runs, newest first, without their images.</summary>
    Task<Result<IReadOnlyList<ImageEvaluationSummaryResponseDto>>> ListEvaluationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one stored scoring run with every scored image.</summary>
    Task<Result<ImageEvaluationDetailResponseDto>> GetEvaluationAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Stores an uploaded scoring run and the thumbnails it carries.</summary>
    Task<Result<ImageEvaluationSummaryResponseDto>> ImportEvaluationAsync(
        ImportImageEvaluationRequestDto request,
        string? uploadedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a stored scoring run and its thumbnails.</summary>
    Task<Result> DeleteEvaluationAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Counts the design images saved in a period by image provider and prompt version.</summary>
    Task<Result<ImageQualityLiveStatsResponseDto>> GetLiveStatsAsync(
        GetImageQualityLiveStatsRequestDto request,
        CancellationToken cancellationToken = default);
}
