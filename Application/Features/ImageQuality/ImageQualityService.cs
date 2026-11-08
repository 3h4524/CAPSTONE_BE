using System.Collections.Concurrent;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.ImageQuality.Common;
using APCS.Application.Features.ImageQuality.Dtos.Request;
using APCS.Application.Features.ImageQuality.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.ImageQuality;

/// <summary>
/// Keeps the uploaded scoring runs (scores in <c>image_eval_runs</c>, thumbnails in public image storage) and works out
/// the live generation stats from the design images and job logs.
/// </summary>
public sealed class ImageQualityService(
    IRepository<ImageEvalRun> runs,
    IRepository<DesignImage> designImages,
    IRepository<BatchJob> batchJobs,
    IRepository<BatchJobLog> batchJobLogs,
    IPublicImageService publicImages,
    IValidator<ImportImageEvaluationRequestDto> importValidator,
    TimeProvider timeProvider,
    ILogger<ImageQualityService> logger) : IImageQualityService
{
    private const int ParallelThumbnailUploads = 4;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ImageEvaluationSummaryResponseDto>>> ListEvaluationsAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await runs.Query()
            .OrderByDescending(run => run.CreatedAt)
            .ToListAsync(cancellationToken);

        IReadOnlyList<ImageEvaluationSummaryResponseDto> summaries = entities
            .Select(entity => ToSummary(entity, ImageEvalReport.FromJson(entity.Report)))
            .ToList();
        return Result.Success(summaries);
    }

    /// <inheritdoc />
    public async Task<Result<ImageEvaluationDetailResponseDto>> GetEvaluationAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await runs.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<ImageEvaluationDetailResponseDto>(NotFound());
        }

        var report = ImageEvalReport.FromJson(entity.Report);
        return Result.Success(new ImageEvaluationDetailResponseDto(
            entity.Id,
            entity.Name,
            entity.Notes,
            ToUtcOffset(entity.CreatedAt),
            entity.UploadedBy,
            entity.ImageCount,
            entity.ProductCount,
            report.Versions,
            report.Images));
    }

    /// <inheritdoc />
    public async Task<Result<ImageEvaluationSummaryResponseDto>> ImportEvaluationAsync(
        ImportImageEvaluationRequestDto request,
        string? uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var validation = await importValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<ImageEvaluationSummaryResponseDto>(validation.ToValidationError());
        }

        var runId = Guid.NewGuid();
        var thumbnails = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await Parallel.ForEachAsync(
                request.Images.Where(image => image.Thumbnail is not null),
                new ParallelOptions { MaxDegreeOfParallelism = ParallelThumbnailUploads, CancellationToken = cancellationToken },
                async (image, token) => thumbnails[image.Id] = await UploadThumbnailAsync(runId, image, token));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Storing the thumbnails of scoring run {RunId} failed; removing the ones stored.", runId);
            await DeleteThumbnailsAsync(runId, thumbnails.Keys);
            return Result.Failure<ImageEvaluationSummaryResponseDto>(Error.Failure(
                ErrorCodes.ImageEvaluationThumbnailUploadFailed,
                "The thumbnails could not be stored, so nothing was saved. Try the upload again."));
        }

        var report = BuildReport(request, thumbnails);
        var entity = new ImageEvalRun
        {
            Id = runId,
            Name = request.Name.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ImageCount = report.Images.Count,
            ProductCount = report.Images.Select(image => image.Product).Distinct(StringComparer.Ordinal).Count(),
            VersionCount = report.Versions.Count,
            Report = report.ToJson(),
            UploadedBy = uploadedBy,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };

        try
        {
            await runs.AddAsync(entity, saveChange: true, cancellationToken);
        }
        catch
        {
            await DeleteThumbnailsAsync(runId, thumbnails.Keys);
            throw;
        }

        return Result.Success(ToSummary(entity, report));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteEvaluationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await runs.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure(NotFound());
        }

        var thumbnailIds = ImageEvalReport.FromJson(entity.Report).Images
            .Where(image => image.ThumbnailUrl is not null)
            .Select(image => image.Id)
            .ToList();

        await runs.RemoveAsync(entity, saveChange: true, cancellationToken);
        await DeleteThumbnailsAsync(entity.Id, thumbnailIds);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<ImageQualityLiveStatsResponseDto>> GetLiveStatsAsync(
        GetImageQualityLiveStatsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = request.FromUtc?.UtcDateTime;
        var toUtc = request.ToUtc?.UtcDateTime;
        if (fromUtc is not null && toUtc is not null && fromUtc >= toUtc)
        {
            return Result.Failure<ImageQualityLiveStatsResponseDto>(
                Error.Validation("The start of the period must come before its end."));
        }

        var imageQuery = designImages.Query().Where(image => image.DeletedAt == null);
        var logQuery = batchJobLogs.Query().Where(log => ImageQualityLiveCalculator.EventTypes.Contains(log.EventType));
        if (fromUtc is not null)
        {
            imageQuery = imageQuery.Where(image => image.CreatedAt >= fromUtc);
            logQuery = logQuery.Where(log => log.CreatedAt >= fromUtc);
        }

        if (toUtc is not null)
        {
            imageQuery = imageQuery.Where(image => image.CreatedAt < toUtc);
            logQuery = logQuery.Where(log => log.CreatedAt < toUtc);
        }

        var images = await imageQuery
            .Select(image => new LiveImageRow(
                image.CreatedAt ?? DateTime.MinValue,
                image.ImageGeneratorModel,
                image.ApprovalStatus,
                image.GenerationTimeSeconds,
                image.GenerationMetadata,
                image.BatchJobId,
                image.ProductId,
                image.ApiUsageRecord != null ? image.ApiUsageRecord.CostUsd : null))
            .ToListAsync(cancellationToken);
        var logs = await logQuery
            .Select(log => new LiveLogRow(log.BatchJobId, log.EventType, log.CreatedAt ?? DateTime.MinValue))
            .ToListAsync(cancellationToken);

        var jobIds = images
            .Where(image => image.BatchJobId is not null)
            .Select(image => image.BatchJobId!.Value)
            .Concat(logs.Select(log => log.BatchJobId))
            .Distinct()
            .ToList();
        var jobConfigs = await batchJobs.Query()
            .Where(job => jobIds.Contains(job.Id))
            .Select(job => new { job.Id, job.Config })
            .ToDictionaryAsync(job => job.Id, job => job.Config, cancellationToken);

        return Result.Success(new ImageQualityLiveStatsResponseDto(
            timeProvider.GetUtcNow(),
            ImageQualityLiveCalculator.Calculate(images, logs, jobConfigs)));
    }

    private async Task<string> UploadThumbnailAsync(
        Guid runId,
        ImageEvalImageRequestDto image,
        CancellationToken cancellationToken)
    {
        var thumbnail = ImageEvalThumbnail.Decode(image.Thumbnail)
            ?? throw new InvalidOperationException($"The thumbnail of image {image.Id} passed validation but cannot be read.");
        await using var stream = new MemoryStream(thumbnail.Bytes, writable: false);
        return await publicImages.UploadImageAsync(
            new UploadFileDto($"{image.Id}.{thumbnail.Extension}", thumbnail.MimeType, thumbnail.Bytes.Length, stream),
            ThumbnailKey(runId, image.Id),
            cancellationToken);
    }

    private async Task DeleteThumbnailsAsync(Guid runId, IEnumerable<string> imageIds)
    {
        foreach (var imageId in imageIds)
        {
            try
            {
                await publicImages.DeleteImageAsync(ThumbnailKey(runId, imageId), CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Thumbnail {ImageId} of scoring run {RunId} could not be deleted.", imageId, runId);
            }
        }
    }

    private static ImageEvalReport BuildReport(
        ImportImageEvaluationRequestDto request,
        IReadOnlyDictionary<string, string> thumbnails)
    {
        var images = request.Images
            .Select(image => new ImageEvalImageResponseDto(
                image.Id,
                image.Version,
                image.Product.Trim(),
                image.ProductType,
                image.ExpectedText,
                image.ImageUrl,
                thumbnails.GetValueOrDefault(image.Id),
                image.OcrText ?? string.Empty,
                image.TextOk,
                image.TextOkOcr,
                image.TextClear,
                image.Note ?? string.Empty,
                image.Cer,
                image.LayoutOk,
                image.NotCut,
                image.Centred,
                image.SizeOk,
                image.Musiq,
                image.TopiqNr,
                image.ClipIqaPlus,
                image.Liqe,
                image.LaionAes,
                image.ClipOverall,
                image.ClipElements))
            .ToList();
        var versions = request.Versions
            .Select(version => new ImageEvalVersionResponseDto(
                version.Key,
                version.Label.Trim(),
                version.Provider,
                version.Phase,
                version.Order,
                images.Count(image => image.Version == version.Key)))
            .ToList();
        return new ImageEvalReport(versions, images);
    }

    private static ImageEvaluationSummaryResponseDto ToSummary(ImageEvalRun entity, ImageEvalReport report) =>
        new(
            entity.Id,
            entity.Name,
            entity.Notes,
            ToUtcOffset(entity.CreatedAt),
            entity.UploadedBy,
            entity.ImageCount,
            entity.ProductCount,
            report.Versions);

    private static string ThumbnailKey(Guid runId, string imageId) => $"image-eval/{runId:N}/{imageId}";

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static Error NotFound() =>
        Error.NotFound(ErrorCodes.ImageEvaluationNotFound, "This scoring run no longer exists.");
}
