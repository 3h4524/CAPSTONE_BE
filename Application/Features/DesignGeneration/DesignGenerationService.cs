using System.Text.Json.Nodes;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Application.Features.DesignGeneration.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.DesignGeneration;

public sealed class DesignGenerationService(
    ICurrentUser currentUser,
    IRepository<Batch> batches,
    IRepository<BatchJob> batchJobs,
    IRepository<BatchJobProduct> batchJobProducts,
    IRepository<Product> products,
    IRepository<AiPrompt> aiPrompts,
    IRepository<DesignImage> designImages,
    IRepository<ApiUsageRecord> apiUsageRecords,
    IRepository<BatchJobLog> batchJobLogs,
    IRepository<DesignTemplate> designTemplates,
    IRepository<StyleArtPreset> styleArtPresets,
    IApiKeyRepository apiKeys,
    IApiKeyCredentials apiKeyCredentials,
    ISubscriptionRepository subscriptions,
    IUsageStatisticRepository usageStatistics,
    IImageGenerationProvider imageProvider,
    IPublicImageService publicImages,
    IDesignBackgroundRemover backgroundRemover,
    IDesignGenerationQueue queue,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<StartGenerationRequestDto> validator) : IDesignGenerationService
{
    private const string GeminiProvider = "gemini";

    // Products whose designs are printed as a standalone graphic on the item, not edge to edge.
    private static readonly HashSet<string> GraphicPrintTypes = new(StringComparer.OrdinalIgnoreCase) { "tshirt", "hoodie", "tote_bag" };

    // Sent with the prompt (not stored in it) so the result has a plain backdrop that can be removed.
    private const string GraphicPrintInstruction =
        "Output only the print-ready artwork, centered, on a plain solid white background. "
        + "No t-shirt, mockup, fabric, paper or photo backdrop, and no drop shadow around the artwork.";
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(3);

    private const string CancelRequestedEvent = "cancel_requested";
    private const string CancelHonoredEvent = "cancel_honored";
    private const string CancelledMessage = "Cancelled by the user.";
    internal const string ImageTimingEvent = "image_timing";

    public async Task<Result<StartGenerationResponseDto>> StartAsync(
        Guid batchJobId,
        StartGenerationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.Unauthenticated("start"));

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<StartGenerationResponseDto>(validation.ToValidationError());

        var job = await batchJobs.Query()
            .SingleOrDefaultAsync(x => x.Id == batchJobId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (job is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.JobNotFound());
        if (!string.Equals(job.Status, BatchJobStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NotDraft());

        // BR52: only one active job per batch at a time.
        var hasActiveJob = await batchJobs.Query().AnyAsync(x => x.BatchId == job.BatchId && x.Id != job.Id
            && x.DeletedAt == null && BatchJobStatuses.Active.Contains(x.Status.ToLower()), cancellationToken);
        if (hasActiveJob) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.ActiveJobExists());

        var rows = await batchJobProducts.Query()
            .Where(x => x.BatchJobId == job.Id && x.Status == BatchJobProductStatuses.Pending)
            .OrderBy(x => x.SequenceOrder)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NoPendingProducts());

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // BR51: a valid, connected Gemini key must exist before the job can start.
        var ownedKeys = await apiKeys.ListOwnedAsync(userId, cancellationToken);
        var geminiKey = ownedKeys.FirstOrDefault(key =>
            string.Equals(key.ServiceProvider, GeminiProvider, StringComparison.OrdinalIgnoreCase)
            && key.IsActive == true && key.LastCheckSucceeded == true
            && (!key.ExpiresAt.HasValue || key.ExpiresAt > now));
        if (geminiKey is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.MissingApiKey());

        // BR50: enough image-generation quota left this billing period.
        var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        if (subscription is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NoActivePlan());
        var today = DateOnly.FromDateTime(now);
        var usage = await usageStatistics.GetCurrentPeriodAsync(userId, today, cancellationToken);
        var imagesToGenerate = rows.Count * request.VariationCount;
        var remainingQuota = subscription.Plan.ImageGenerationQuota - (usage?.ImagesGenerated ?? 0);
        if (imagesToGenerate > remainingQuota)
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.InsufficientQuota());

        var productIds = rows.Where(row => row.ProductId.HasValue).Select(row => row.ProductId!.Value).ToList();
        var productList = await products.Query().Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);

        // BR98: an explicit template/style selection applies to every product in the job.
        string? overrideStyleName = null;
        if (request.StyleArtPresetId.HasValue)
        {
            var style = await styleArtPresets.Query()
                .SingleOrDefaultAsync(s => s.Id == request.StyleArtPresetId.Value, cancellationToken);
            if (style is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.StyleNotFound());
            overrideStyleName = style.Name;
        }
        if (request.DesignTemplateId.HasValue
            && !await designTemplates.Query().AnyAsync(t => t.Id == request.DesignTemplateId.Value, cancellationToken))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.TemplateNotFound());

        var productById = productList.ToDictionary(p => p.Id);
        foreach (var product in productList)
        {
            if (request.DesignTemplateId.HasValue) product.DesignTemplateId = request.DesignTemplateId;
            if (overrideStyleName is not null) product.StylePreset = overrideStyleName;
            product.ProcessingStatus = BatchJobProductStatuses.Queued;
            product.UpdatedAt = now;
            await products.UpdateAsync(product, cancellationToken: cancellationToken);
        }

        // A product keeps one prompt per version (unique per product), and it may already have prompts from
        // an earlier job, so each new prompt takes the next number instead of always claiming version 1.
        var latestVersionByProduct = (await aiPrompts.Query()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.VersionNumber })
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.ProductId)
            .ToDictionary(group => group.Key, group => group.Max(p => p.VersionNumber));

        // Synthesize and persist the effective AI prompt for every pending row (SRS 3.5.10).
        foreach (var row in rows)
        {
            var product = row.ProductId.HasValue && productById.TryGetValue(row.ProductId.Value, out var owned) ? owned : null;
            var defaults = await PromptDefaultsResolver.ResolveAsync(product, designTemplates, styleArtPresets, cancellationToken);
            var effective = defaults with
            {
                Subject = row.CustomSubject ?? defaults.Subject,
                ArtStyle = row.CustomArtStyle ?? defaults.ArtStyle,
                MoodTone = row.CustomMoodTone ?? defaults.MoodTone,
                NegativeTerms = row.CustomNegativeTerms ?? string.Empty,
                // The job-level wording applies to products that have no instructions of their own.
                Instructions = row.CustomInstructions ?? request.Instructions?.Trim() ?? string.Empty
            };
            var generatedPrompt = PromptComposer.Compose(
                defaults.BasePrompt, effective.Subject, effective.ArtStyle, effective.MoodTone,
                effective.NegativeTerms, effective.Instructions, defaults.Niche, defaults.StyleModifiers,
                defaults.Keywords, defaults.Description);

            var promptProductId = row.ProductId!.Value;
            var promptVersion = latestVersionByProduct.GetValueOrDefault(promptProductId) + 1;
            latestVersionByProduct[promptProductId] = promptVersion;

            await aiPrompts.AddAsync(new AiPrompt
            {
                Id = Guid.NewGuid(),
                ProductId = promptProductId,
                DesignTemplateId = product?.DesignTemplateId,
                OriginalDescription = product?.InputDescription ?? string.Empty,
                SystemPrompt = defaults.BasePrompt,
                GeneratedPrompt = generatedPrompt,
                VersionNumber = promptVersion,
                BatchJobProductId = row.Id,
                CreatedAt = now
            }, cancellationToken: cancellationToken);

            row.Status = BatchJobProductStatuses.GeneratingImage;
            row.StartedAt = now;
            row.UpdatedAt = now;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        job.Status = BatchJobStatuses.Queued;
        job.Config = WriteConfig(job.Config, request.VariationCount, request.AspectRatio, request.Instructions, request.RequireApproval, request.WorkflowId);
        job.StartedAt = now;
        job.UpdatedAt = now;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);

        var batch = await batches.Query().SingleOrDefaultAsync(b => b.Id == job.BatchId, cancellationToken);
        if (batch is not null)
        {
            batch.Status = "processing";
            batch.UpdatedAt = now;
            await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        }

        // Commit before enqueueing: the background worker opens its own scope/DbContext and must see
        // these rows already saved.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        queue.Enqueue(job.Id);

        return Result.Success(new StartGenerationResponseDto(job.Id, rows.Count, job.Status));
    }

    public async Task<Result<StartGenerationResponseDto>> RetryFailedAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.Unauthenticated("retry"));

        var job = await batchJobs.Query()
            .SingleOrDefaultAsync(x => x.Id == batchJobId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (job is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.JobNotFound());
        if (job.Status is not (BatchJobStatuses.Failed or BatchJobStatuses.PartiallyCompleted or BatchJobStatuses.Completed))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NotRetryable());

        // BR52: no other active job for the same batch.
        var hasActiveJob = await batchJobs.Query().AnyAsync(x => x.BatchId == job.BatchId && x.Id != job.Id
            && x.DeletedAt == null && BatchJobStatuses.Active.Contains(x.Status.ToLower()), cancellationToken);
        if (hasActiveJob) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.ActiveJobExists());

        var failedRows = await batchJobProducts.Query()
            .Where(x => x.BatchJobId == job.Id && x.Status == BatchJobProductStatuses.Failed)
            .OrderBy(x => x.SequenceOrder)
            .ToListAsync(cancellationToken);
        // A product that was set back to pending, or already put into a later job, is no longer this job's to retry.
        var failedProductIds = failedRows.Where(row => row.ProductId.HasValue).Select(row => row.ProductId!.Value).ToList();
        var resetIds = await products.Query()
            .Where(p => failedProductIds.Contains(p.Id) && p.ProcessingStatus == BatchJobProductStatuses.Pending)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var takenOverIds = await batchJobProducts.Query()
            .Where(row => row.BatchJobId != job.Id && row.ProductId != null && failedProductIds.Contains(row.ProductId.Value)
                && row.CreatedAt > job.CreatedAt)
            .Select(row => row.ProductId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var failedRowCount = failedRows.Count;
        failedRows = failedRows
            .Where(row => row.ProductId is not Guid productId || (!resetIds.Contains(productId) && !takenOverIds.Contains(productId)))
            .ToList();
        if (failedRows.Count == 0) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NoFailedProducts());

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // BR51: a valid, connected Gemini key must still exist.
        var ownedKeys = await apiKeys.ListOwnedAsync(userId, cancellationToken);
        var hasKey = ownedKeys.Any(key =>
            string.Equals(key.ServiceProvider, GeminiProvider, StringComparison.OrdinalIgnoreCase)
            && key.IsActive == true && key.LastCheckSucceeded == true
            && (!key.ExpiresAt.HasValue || key.ExpiresAt > now));
        if (!hasKey) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.MissingApiKey());

        // BR50: a retry consumes quota again.
        var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        if (subscription is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NoActivePlan());
        var (variationCount, _) = ReadConfig(job.Config);
        var usage = await usageStatistics.GetCurrentPeriodAsync(userId, DateOnly.FromDateTime(now), cancellationToken);
        var remainingQuota = subscription.Plan.ImageGenerationQuota - (usage?.ImagesGenerated ?? 0);
        if (failedRows.Count * variationCount > remainingQuota)
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.InsufficientQuota());

        var productIds = failedRows.Where(row => row.ProductId.HasValue).Select(row => row.ProductId!.Value).ToList();
        var productList = await products.Query().Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);
        foreach (var product in productList)
        {
            product.ProcessingStatus = BatchJobProductStatuses.Queued;
            product.UpdatedAt = now;
            await products.UpdateAsync(product, cancellationToken: cancellationToken);
        }

        // The synthesized AiPrompt of each row is kept, so only the provider call is repeated.
        foreach (var row in failedRows)
        {
            row.Status = BatchJobProductStatuses.GeneratingImage;
            row.ErrorMessage = null;
            row.StartedAt = now;
            row.CompletedAt = null;
            row.UpdatedAt = now;
            row.RetryCount = (row.RetryCount ?? 0) + 1;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        var total = job.TotalProducts ?? failedRows.Count;
        job.ProcessedProducts = Math.Max(0, (job.ProcessedProducts ?? 0) - failedRows.Count);
        job.FailedProducts = failedRowCount - failedRows.Count;
        job.ProgressPercentage = total > 0 ? Math.Round(100m * job.ProcessedProducts.Value / total, 0) : 0;
        job.Status = BatchJobStatuses.Queued;
        job.CompletedAt = null;
        job.UpdatedAt = now;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);

        var batch = await batches.Query().SingleOrDefaultAsync(b => b.Id == job.BatchId, cancellationToken);
        if (batch is not null)
        {
            batch.Status = "processing";
            batch.UpdatedAt = now;
            await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        }

        await LogAsync(job.Id, null, "info", "retry_requested", $"Retrying {failedRows.Count} failed product(s).", cancellationToken);

        // Commit before enqueueing so the worker, which uses its own DbContext, sees the reset rows.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        queue.Enqueue(job.Id);

        return Result.Success(new StartGenerationResponseDto(job.Id, failedRows.Count, job.Status));
    }

    public async Task ProcessBatchJobAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        var job = await batchJobs.Query().SingleOrDefaultAsync(x => x.Id == batchJobId, cancellationToken);
        if (job is null) return;

        var ownedKeys = await apiKeys.ListOwnedAsync(job.UserId, cancellationToken);
        var geminiKey = ownedKeys.FirstOrDefault(key =>
            string.Equals(key.ServiceProvider, GeminiProvider, StringComparison.OrdinalIgnoreCase) && key.IsActive == true);
        if (geminiKey is null)
        {
            // Defensive only: StartAsync already required a valid key moments earlier.
            await LogAsync(job.Id, null, "error", "generation_aborted", "Gemini API key is no longer available.", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }
        var apiKeyPlain = apiKeyCredentials.Unprotect(geminiKey.KeyValueEncrypted);
        var requireApproval = ReadRequireApproval(job.Config);
        // Automatic approval marks the images and the product approved as they are made; manual leaves them for the Seller.
        var reviewedStatus = requireApproval == false ? BatchJobProductStatuses.Approved : BatchJobProductStatuses.ImageReviewRequired;
        var (variationCount, aspectRatio) = ReadConfig(job.Config);

        job.Status = BatchJobStatuses.Running;
        job.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var rows = await batchJobProducts.Query()
            .Where(x => x.BatchJobId == job.Id && x.Status == BatchJobProductStatuses.GeneratingImage)
            .OrderBy(x => x.SequenceOrder)
            .ToListAsync(cancellationToken);

        // Once the user cancels, the product being generated finishes and every one after it is failed.
        var cancelled = false;
        foreach (var row in rows)
        {
            cancelled = cancelled || await CancelPendingAsync(job.Id, cancellationToken);
            if (cancelled)
            {
                await CancelRowAsync(job, row, cancellationToken);
                continue;
            }

            var prompt = await aiPrompts.Query()
                .Where(p => p.BatchJobProductId == row.Id)
                .OrderByDescending(p => p.VersionNumber)
                .FirstOrDefaultAsync(cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;

            if (prompt is null)
            {
                row.Status = BatchJobProductStatuses.Failed;
                row.ErrorMessage = "No synthesized prompt was found for this product.";
                row.CompletedAt = now;
                row.UpdatedAt = now;
                await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
                job.FailedProducts = (job.FailedProducts ?? 0) + 1;
                job.ProcessedProducts = (job.ProcessedProducts ?? 0) + 1;
                await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                continue;
            }

            var productType = row.ProductId is Guid productId
                ? await products.Query().Where(p => p.Id == productId).Select(p => p.ProductType).FirstOrDefaultAsync(cancellationToken)
                : null;
            var graphicPrint = productType is not null && GraphicPrintTypes.Contains(productType);
            var requestPrompt = graphicPrint ? $"{prompt.GeneratedPrompt}\n{GraphicPrintInstruction}" : prompt.GeneratedPrompt;

            var successCount = 0;
            for (var variation = 0; variation < variationCount; variation++)
            {
                var started = timeProvider.GetUtcNow();

                // Hard cap for one image (provider call + upload) so nothing can hang the job forever.
                using var stepTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stepTimeout.CancelAfter(StepTimeout);

                await LogAsync(job.Id, row.Id, "info", "generation_started",
                    $"Requesting image {variation + 1}/{variationCount} from the provider.", cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                ImageGenerationResult result;
                try
                {
                    result = await imageProvider.GenerateAsync(apiKeyPlain, requestPrompt, aspectRatio, stepTimeout.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // Never let one provider failure strand the whole job in "running".
                    result = ImageGenerationResult.Failed(ex is OperationCanceledException
                        ? "Image generation timed out."
                        : $"Unexpected provider error: {ex.Message}", "unknown");
                }
                var elapsedSeconds = (decimal)(timeProvider.GetUtcNow() - started).TotalSeconds;

                if (!result.IsSuccess)
                {
                    await LogAsync(job.Id, row.Id, "warning",
                        result.IsBlocked ? "generation_blocked" : "generation_failed",
                        result.ErrorMessage ?? "Unknown error", cancellationToken);
                    continue;
                }

                var cutOutStarted = timeProvider.GetTimestamp();
                var (imageBytes, mimeType) = graphicPrint
                    ? CutOutBackground(result.ImageBytes!, result.MimeType!)
                    : (result.ImageBytes!, result.MimeType!);
                var cutOutTime = timeProvider.GetElapsedTime(cutOutStarted);

                // Keyed by the image's own id (created before upload) so each file maps 1:1 to its row
                // and a later regenerate with the same prompt can never overwrite an existing image.
                var designImageId = Guid.NewGuid();
                var storageKey = $"design-images/{designImageId:N}";
                PublicImageUploadResult uploaded;
                var uploadStarted = timeProvider.GetTimestamp();
                using (var stream = new MemoryStream(imageBytes))
                {
                    try
                    {
                        // Gemini returns JPEG, so the stored size comes from the upload rather than the bytes.
                        uploaded = await publicImages.UploadImageWithMetadataAsync(
                            new UploadFileDto($"{Guid.NewGuid()}.{FileExtension(mimeType)}", mimeType, stream.Length, stream),
                            storageKey,
                            stepTimeout.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        await LogAsync(job.Id, row.Id, "error", "upload_failed",
                            ex is OperationCanceledException ? "Image upload timed out." : ex.Message, cancellationToken);
                        continue;
                    }
                }
                var uploadTime = timeProvider.GetElapsedTime(uploadStarted);

                var usageRecord = new ApiUsageRecord
                {
                    Id = Guid.NewGuid(),
                    UserId = job.UserId,
                    BatchJobId = job.Id,
                    ProductId = row.ProductId,
                    Provider = GeminiProvider,
                    Feature = "image_generation",
                    ModelName = result.ModelUsed,
                    RequestUnits = 1,
                    CostUsd = result.EstimatedCostUsd,
                    Status = "success",
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime
                };
                await apiUsageRecords.AddAsync(usageRecord, cancellationToken: cancellationToken);

                var (width, height) = uploaded.WidthPx > 0 && uploaded.HeightPx > 0
                    ? (uploaded.WidthPx, uploaded.HeightPx)
                    : TryReadPngDimensions(imageBytes);
                await designImages.AddAsync(new DesignImage
                {
                    Id = designImageId,
                    ProductId = row.ProductId!.Value,
                    AiPromptId = prompt.Id,
                    BatchJobId = job.Id,
                    BatchJobProductId = row.Id,
                    ApiUsageRecordId = usageRecord.Id,
                    ImageGeneratorModel = result.ModelUsed,
                    StorageProvider = "cloudinary",
                    StorageKey = storageKey,
                    ImageUrl = uploaded.Url,
                    ImageWidthPx = width,
                    ImageHeightPx = height,
                    FileFormat = FileExtension(mimeType),
                    FileSizeMb = Math.Round(imageBytes.Length / 1024m / 1024m, 3),
                    ApprovalStatus = requireApproval == false ? ApprovalStatuses.Approved : ApprovalStatuses.Pending,
                    // 1-based: the column's DB default is 1, so EF drops a 0 and the DB stores 1 instead.
                    VariationIndex = variation + 1,
                    GenerationTimeSeconds = elapsedSeconds,
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime
                }, cancellationToken: cancellationToken);
                await LogTimingAsync(job.Id, row.Id, variation + 1, variationCount,
                    TimeSpan.FromSeconds((double)elapsedSeconds), cutOutTime, uploadTime, cancellationToken);

                successCount++;
            }

            row.Status = successCount > 0 ? reviewedStatus : BatchJobProductStatuses.Failed;
            row.ErrorMessage = successCount == 0 ? "All requested variations failed or were blocked." : null;
            row.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            row.UpdatedAt = row.CompletedAt;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);

            if (row.ProductId.HasValue)
            {
                var product = await products.Query().SingleOrDefaultAsync(p => p.Id == row.ProductId.Value, cancellationToken);
                if (product is not null)
                {
                    product.ProcessingStatus = successCount > 0 ? reviewedStatus : BatchJobProductStatuses.Failed;
                    product.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
                    await products.UpdateAsync(product, cancellationToken: cancellationToken);
                }
            }

            if (successCount > 0) await IncrementImagesGeneratedAsync(job.UserId, successCount, cancellationToken);

            job.ProcessedProducts = (job.ProcessedProducts ?? 0) + 1;
            if (successCount == 0) job.FailedProducts = (job.FailedProducts ?? 0) + 1;
            job.ProgressPercentage = job.TotalProducts is > 0
                ? Math.Round(100m * job.ProcessedProducts.Value / job.TotalProducts.Value, 0)
                : job.ProgressPercentage;
            job.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);

            // Persist per-row: partial progress survives even if a later row throws or the process stops.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // A cancel that arrived during the last product is consumed too, so a later retry is not stopped by it.
        if (cancelled || await CancelPendingAsync(job.Id, cancellationToken))
            await LogAsync(job.Id, null, "info", CancelHonoredEvent, "Generation stopped at the user's request.", cancellationToken);

        var failed = job.FailedProducts ?? 0;
        job.Status = failed == 0 ? BatchJobStatuses.Completed
            : failed >= (job.TotalProducts ?? 0) ? BatchJobStatuses.Failed
            : BatchJobStatuses.PartiallyCompleted;
        job.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        job.UpdatedAt = job.CompletedAt;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);

        var batch = await batches.Query().SingleOrDefaultAsync(b => b.Id == job.BatchId, cancellationToken);
        if (batch is not null)
        {
            batch.Status = "completed";
            batch.UpdatedAt = job.CompletedAt;
            await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<BatchJobDetailDto>> GetJobAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<BatchJobDetailDto>(DesignGenerationErrors.Unauthenticated("view"));

        var job = await batchJobs.Query()
            .SingleOrDefaultAsync(x => x.Id == batchJobId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (job is null) return Result.Failure<BatchJobDetailDto>(DesignGenerationErrors.JobNotFound());

        var batchName = await batches.Query().Where(b => b.Id == job.BatchId).Select(b => b.Name).SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        var rows = await batchJobProducts.Query()
            .Where(r => r.BatchJobId == job.Id)
            .OrderBy(r => r.SequenceOrder)
            .ToListAsync(cancellationToken);
        var productIds = rows.Where(r => r.ProductId.HasValue).Select(r => r.ProductId!.Value).ToList();
        var productInfo = await products.Query().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.ProductType })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var images = await designImages.Query()
            .Where(i => i.BatchJobId == job.Id && i.DeletedAt == null)
            .OrderBy(i => i.VariationIndex)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
        var imagesByRow = images.Where(i => i.BatchJobProductId.HasValue)
            .GroupBy(i => i.BatchJobProductId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<GeneratedImageDto>)g
                .Select(i => new GeneratedImageDto(i.Id, i.ImageUrl, i.VariationIndex, i.ImageWidthPx, i.ImageHeightPx, i.ApprovalStatus))
                .ToList());

        var rowDtos = rows.Select(r =>
        {
            var info = r.ProductId.HasValue && productInfo.TryGetValue(r.ProductId.Value, out var found) ? found : null;
            return new BatchJobProductResultDto(
                r.Id, r.ProductId, info?.Name ?? string.Empty, info?.ProductType ?? string.Empty,
                r.SequenceOrder, r.Status, r.ErrorMessage,
                imagesByRow.TryGetValue(r.Id, out var list) ? list : []);
        }).ToList();

        var counters = new BatchJobCountersDto(
            rows.Count(r => r.Status is BatchJobProductStatuses.Pending or BatchJobProductStatuses.Queued),
            rows.Count(r => r.Status == BatchJobProductStatuses.GeneratingImage),
            rows.Count(r => r.Status is BatchJobProductStatuses.ImageReviewRequired or BatchJobProductStatuses.Approved),
            rows.Count(r => r.Status == BatchJobProductStatuses.Failed));

        var (variationCount, aspectRatio) = ReadConfig(job.Config);
        return Result.Success(new BatchJobDetailDto(
            job.Id, job.BatchId, batchName, job.Status,
            job.TotalProducts ?? rows.Count, job.ProcessedProducts ?? 0, job.FailedProducts ?? 0,
            job.ProgressPercentage ?? 0, job.StartedAt, job.CompletedAt,
            variationCount, aspectRatio, counters, rowDtos, ReadRequireApproval(job.Config), ReadWorkflowId(job.Config)));
    }

    public async Task<Result<IReadOnlyList<BatchJobSummaryDto>>> ListJobsForBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<BatchJobSummaryDto>>(DesignGenerationErrors.Unauthenticated("view"));

        if (!await batches.Query().AnyAsync(b => b.Id == batchId && b.UserId == userId && b.DeletedAt == null, cancellationToken))
            return Result.Failure<IReadOnlyList<BatchJobSummaryDto>>(DesignGenerationErrors.BatchNotFound());

        var list = await batchJobs.Query()
            .Where(j => j.BatchId == batchId && j.UserId == userId && j.DeletedAt == null)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new { j.Id, j.Status, j.TotalProducts, j.ProcessedProducts, j.FailedProducts, j.CreatedAt, j.StartedAt, j.Config })
            .ToListAsync(cancellationToken);
        // The workflow sits inside the config JSON, so it is read here rather than in the query.
        return Result.Success<IReadOnlyList<BatchJobSummaryDto>>(list
            .Select(j => new BatchJobSummaryDto(j.Id, j.Status, j.TotalProducts ?? 0, j.ProcessedProducts ?? 0, j.FailedProducts ?? 0,
                j.CreatedAt, j.StartedAt, ReadWorkflowId(j.Config)))
            .ToList());
    }

    // One job processes many products with the same DbContext. The usage row must therefore be
    // loaded (or created) once and the same instance reused: querying again would return a second
    // instance with the same key, which EF refuses to track ("another instance ... already tracked").
    private UsageStatistic? cachedUsage;

    private async Task IncrementImagesGeneratedAsync(Guid userId, int count, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        if (cachedUsage is null || cachedUsage.UserId != userId)
        {
            cachedUsage = await usageStatistics.Query()
                .SingleOrDefaultAsync(u => u.UserId == userId && u.BillingPeriodStart <= today && u.BillingPeriodEnd >= today, cancellationToken);

            if (cachedUsage is null)
            {
                var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
                var (periodStart, periodEnd) = UsagePeriod.Containing(today, subscription?.RenewalDate ?? today.AddMonths(1));
                cachedUsage = new UsageStatistic
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BillingPeriodStart = periodStart,
                    BillingPeriodEnd = periodEnd,
                    ImagesGenerated = count,
                    UpdatedAt = now,
                    CreatedAt = now
                };
                // A brand-new row must only be Added: calling UpdateAsync on it before it is saved would
                // make EF emit an UPDATE for a row that does not exist yet ("affected 0 row(s)").
                await usageStatistics.AddAsync(cachedUsage, cancellationToken: cancellationToken);
                return;
            }
        }

        cachedUsage.ImagesGenerated = (cachedUsage.ImagesGenerated ?? 0) + count;
        cachedUsage.UpdatedAt = now;
        await usageStatistics.UpdateAsync(cachedUsage, cancellationToken: cancellationToken);
    }

    public async Task FailJobAsync(Guid batchJobId, string reason, CancellationToken cancellationToken = default)
    {
        var job = await batchJobs.Query().SingleOrDefaultAsync(x => x.Id == batchJobId, cancellationToken);
        if (job is null || job.Status is BatchJobStatuses.Completed or BatchJobStatuses.PartiallyCompleted or BatchJobStatuses.Failed)
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stuck = await batchJobProducts.Query()
            .Where(x => x.BatchJobId == job.Id && x.Status == BatchJobProductStatuses.GeneratingImage)
            .ToListAsync(cancellationToken);
        foreach (var row in stuck)
        {
            row.Status = BatchJobProductStatuses.Failed;
            row.ErrorMessage = reason;
            row.CompletedAt = now;
            row.UpdatedAt = now;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        job.FailedProducts = Math.Max(job.FailedProducts ?? 0, stuck.Count);
        job.Status = BatchJobStatuses.Failed;
        job.CompletedAt = now;
        job.UpdatedAt = now;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);
        await LogAsync(job.Id, null, "error", "job_failed", reason, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task LogAsync(Guid batchJobId, Guid? batchJobProductId, string level, string eventType, string message, CancellationToken cancellationToken) =>
        await batchJobLogs.AddAsync(new BatchJobLog
        {
            Id = Guid.NewGuid(),
            BatchJobId = batchJobId,
            BatchJobProductId = batchJobProductId,
            LogLevel = level,
            EventType = eventType,
            Message = message,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken: cancellationToken);

    /// <summary>
    /// Records where the time of one image went, so a slow job can be explained from its log: the provider
    /// call, removing the backdrop, and storing the image.
    /// </summary>
    private async Task LogTimingAsync(Guid batchJobId, Guid batchJobProductId, int variation, int variationCount,
        TimeSpan provider, TimeSpan cutOut, TimeSpan upload, CancellationToken cancellationToken) =>
        await batchJobLogs.AddAsync(new BatchJobLog
        {
            Id = Guid.NewGuid(),
            BatchJobId = batchJobId,
            BatchJobProductId = batchJobProductId,
            LogLevel = "debug",
            EventType = ImageTimingEvent,
            Message = FormattableString.Invariant(
                $"Image {variation}/{variationCount}: provider {provider.TotalSeconds:0.0}s, cut-out {cutOut.TotalSeconds:0.0}s, upload {upload.TotalSeconds:0.0}s."),
            Details = new JsonObject
            {
                ["providerMs"] = (long)provider.TotalMilliseconds,
                ["cutOutMs"] = (long)cutOut.TotalMilliseconds,
                ["uploadMs"] = (long)upload.TotalMilliseconds,
            }.ToJsonString(),
            DurationMs = (int)(provider + cutOut + upload).TotalMilliseconds,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken: cancellationToken);

    private static string WriteConfig(string config, int variationCount, string aspectRatio, string? instructions, bool? requireApproval, Guid? workflowId)
    {
        JsonObject root;
        try { root = JsonNode.Parse(config)?.AsObject() ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { root = new JsonObject(); }

        root[GenerationConfigKeys.VariationCount] = variationCount;
        root[GenerationConfigKeys.AspectRatio] = aspectRatio;
        if (string.IsNullOrWhiteSpace(instructions)) root.Remove(GenerationConfigKeys.Instructions);
        else root[GenerationConfigKeys.Instructions] = instructions.Trim();
        if (requireApproval is null) root.Remove(GenerationConfigKeys.RequireApproval);
        else root[GenerationConfigKeys.RequireApproval] = requireApproval.Value;
        if (workflowId is null) root.Remove(GenerationConfigKeys.WorkflowId);
        else root[GenerationConfigKeys.WorkflowId] = workflowId.Value.ToString();
        return root.ToJsonString();
    }

    /// <summary>
    /// A cancel request is a log row, not a job column: the worker rewrites the whole job row after
    /// every product, so a flag stored on the job would be overwritten before it was seen.
    /// It stays pending until the worker records that it honored it.
    /// </summary>
    private async Task<bool> CancelPendingAsync(Guid batchJobId, CancellationToken cancellationToken)
    {
        var events = await batchJobLogs.Query()
            .Where(log => log.BatchJobId == batchJobId && (log.EventType == CancelRequestedEvent || log.EventType == CancelHonoredEvent))
            .Select(log => new { log.EventType, log.CreatedAt })
            .ToListAsync(cancellationToken);
        var requestedAt = events.Where(e => e.EventType == CancelRequestedEvent).Max(e => e.CreatedAt);
        var honoredAt = events.Where(e => e.EventType == CancelHonoredEvent).Max(e => e.CreatedAt);
        return requestedAt is not null && (honoredAt is null || requestedAt > honoredAt);
    }

    // Fails one product that was still waiting when its job was cancelled; "Retry failed" can run it again.
    private async Task CancelRowAsync(BatchJob job, BatchJobProduct row, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        row.Status = BatchJobProductStatuses.Failed;
        row.ErrorMessage = CancelledMessage;
        row.CompletedAt = now;
        row.UpdatedAt = now;
        await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);

        if (row.ProductId.HasValue)
        {
            var product = await products.Query().SingleOrDefaultAsync(p => p.Id == row.ProductId.Value, cancellationToken);
            if (product is not null)
            {
                product.ProcessingStatus = BatchJobProductStatuses.Failed;
                product.UpdatedAt = now;
                await products.UpdateAsync(product, cancellationToken: cancellationToken);
            }
        }

        job.FailedProducts = (job.FailedProducts ?? 0) + 1;
        job.ProcessedProducts = (job.ProcessedProducts ?? 0) + 1;
        job.ProgressPercentage = job.TotalProducts is > 0
            ? Math.Round(100m * job.ProcessedProducts.Value / job.TotalProducts.Value, 0)
            : job.ProgressPercentage;
        job.UpdatedAt = now;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private const int MaximumApprovalBatch = 500;

    public async Task<Result<ResetProductsResultDto>> ResetProductsAsync(
        Guid batchId,
        ResetProductsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<ResetProductsResultDto>(DesignGenerationErrors.Unauthenticated("reset"));

        var requestedIds = request.ProductIds?.Distinct().ToList();
        if (requestedIds is not null && (requestedIds.Count is 0 or > MaximumApprovalBatch))
            return Result.Failure<ResetProductsResultDto>(Error.Validation($"Choose between 1 and {MaximumApprovalBatch} products."));

        if (!await batches.Query().AnyAsync(b => b.Id == batchId && b.UserId == userId && b.DeletedAt == null, cancellationToken))
            return Result.Failure<ResetProductsResultDto>(DesignGenerationErrors.BatchNotFound());
        // A running job is still writing the status of its products.
        if (await batchJobs.Query().AnyAsync(x => x.BatchId == batchId && x.DeletedAt == null
                && BatchJobStatuses.Active.Contains(x.Status.ToLower()), cancellationToken))
            return Result.Failure<ResetProductsResultDto>(DesignGenerationErrors.ActiveJobExists());

        var candidates = await products.Query()
            .Where(p => p.BatchId == batchId && p.UserId == userId && p.DeletedAt == null
                && (requestedIds == null || requestedIds.Contains(p.Id)))
            .ToListAsync(cancellationToken);
        if (requestedIds is not null && candidates.Count != requestedIds.Count)
            return Result.Failure<ResetProductsResultDto>(DesignGenerationErrors.ProductNotFound());

        // A product under review is resettable once nothing of it is left to decide: every design rejected.
        var underReviewIds = candidates
            .Where(p => p.ProcessingStatus == BatchJobProductStatuses.ImageReviewRequired)
            .Select(p => p.Id)
            .ToList();
        var undecidedIds = await designImages.Query()
            .Where(image => underReviewIds.Contains(image.ProductId) && image.DeletedAt == null
                && image.ApprovalStatus != ApprovalStatuses.Rejected)
            .Select(image => image.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var resettable = candidates
            .Where(p => p.ProcessingStatus == BatchJobProductStatuses.Failed
                || (p.ProcessingStatus == BatchJobProductStatuses.ImageReviewRequired && !undecidedIds.Contains(p.Id)))
            .ToList();

        // Products named one by one must all qualify; "every product" takes the ones that do.
        if (requestedIds is not null && resettable.Count != candidates.Count)
            return Result.Failure<ResetProductsResultDto>(DesignGenerationErrors.NotResettable());
        if (resettable.Count == 0)
            return Result.Failure<ResetProductsResultDto>(DesignGenerationErrors.NothingToReset());

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var product in resettable)
        {
            product.ProcessingStatus = BatchJobProductStatuses.Pending;
            product.UpdatedAt = now;
            await products.UpdateAsync(product, cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new ResetProductsResultDto(resettable.Count));
    }

    public async Task<Result<ImageApprovalResultDto>> SetImageApprovalAsync(
        Guid batchJobId,
        SetImageApprovalRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<ImageApprovalResultDto>(DesignGenerationErrors.Unauthenticated("review"));

        var status = request.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ApprovalStatuses.All.Contains(status))
            return Result.Failure<ImageApprovalResultDto>(Error.Validation($"Status must be one of: {string.Join(", ", ApprovalStatuses.All)}."));
        var requestedIds = request.DesignImageIds?.Distinct().ToList();
        if (requestedIds is not null && (requestedIds.Count is 0 or > MaximumApprovalBatch))
            return Result.Failure<ImageApprovalResultDto>(Error.Validation($"Choose between 1 and {MaximumApprovalBatch} images."));

        var job = await batchJobs.Query()
            .SingleOrDefaultAsync(x => x.Id == batchJobId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (job is null) return Result.Failure<ImageApprovalResultDto>(DesignGenerationErrors.JobNotFound());
        // Images keep arriving while the job runs, and a retry replaces failed ones, so decisions wait for the job to settle.
        if (job.Status is not (BatchJobStatuses.Completed or BatchJobStatuses.PartiallyCompleted))
            return Result.Failure<ImageApprovalResultDto>(DesignGenerationErrors.ApprovalNotAvailable());

        var images = await designImages.Query()
            .Where(image => image.BatchJobId == job.Id && image.DeletedAt == null)
            .ToListAsync(cancellationToken);

        List<DesignImage> targets;
        if (requestedIds is null)
        {
            targets = images.Where(image => image.ApprovalStatus == ApprovalStatuses.Pending).ToList();
        }
        else
        {
            targets = images.Where(image => requestedIds.Contains(image.Id)).ToList();
            if (targets.Count != requestedIds.Count)
                return Result.Failure<ImageApprovalResultDto>(DesignGenerationErrors.ImageNotFound());
        }

        var changed = targets.Where(image => image.ApprovalStatus != status).ToList();
        foreach (var image in changed)
        {
            image.ApprovalStatus = status;
            await designImages.UpdateAsync(image, cancellationToken: cancellationToken);
        }

        await RefreshReviewStatusAsync(images, changed, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ImageApprovalResultDto(
            changed.Count,
            images.Count(image => image.ApprovalStatus == ApprovalStatuses.Pending),
            images.Count(image => image.ApprovalStatus == ApprovalStatuses.Approved),
            images.Count(image => image.ApprovalStatus == ApprovalStatuses.Rejected)));
    }

    // A product counts as approved while at least one of its images is, and goes back to waiting for review when
    // the last approved one is taken back. Products that failed or are still being generated are left alone.
    private async Task RefreshReviewStatusAsync(IReadOnlyList<DesignImage> images, IReadOnlyList<DesignImage> changed, CancellationToken cancellationToken)
    {
        var rowIds = changed.Where(image => image.BatchJobProductId.HasValue).Select(image => image.BatchJobProductId!.Value).Distinct().ToList();
        if (rowIds.Count == 0) return;

        var rows = await batchJobProducts.Query().Where(row => rowIds.Contains(row.Id)).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var row in rows)
        {
            if (row.Status is not (BatchJobProductStatuses.ImageReviewRequired or BatchJobProductStatuses.Approved)) continue;

            var hasApproved = images.Any(image => image.BatchJobProductId == row.Id && image.ApprovalStatus == ApprovalStatuses.Approved);
            var status = hasApproved ? BatchJobProductStatuses.Approved : BatchJobProductStatuses.ImageReviewRequired;
            if (row.Status == status) continue;

            row.Status = status;
            row.UpdatedAt = now;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);

            if (!row.ProductId.HasValue) continue;
            var product = await products.Query().SingleOrDefaultAsync(item => item.Id == row.ProductId.Value, cancellationToken);
            if (product is null) continue;
            product.ProcessingStatus = status;
            product.UpdatedAt = now;
            await products.UpdateAsync(product, cancellationToken: cancellationToken);
        }
    }

    public async Task<Result> CancelAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure(DesignGenerationErrors.Unauthenticated("cancel"));

        var job = await batchJobs.Query()
            .SingleOrDefaultAsync(x => x.Id == batchJobId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (job is null) return Result.Failure(DesignGenerationErrors.JobNotFound());
        if (job.Status is not (BatchJobStatuses.Queued or BatchJobStatuses.Running))
            return Result.Failure(DesignGenerationErrors.NotCancellable());

        // Asking twice changes nothing: the worker is already going to stop.
        if (!await CancelPendingAsync(job.Id, cancellationToken))
        {
            await LogAsync(job.Id, null, "info", CancelRequestedEvent, "Cancellation requested by the user.", cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>The job's approval mode: null for jobs started before approval existed.</summary>
    private static bool? ReadRequireApproval(string config)
    {
        try
        {
            return (bool?)JsonNode.Parse(config)?[GenerationConfigKeys.RequireApproval];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The workflow the job was started from: null when it was started outside one.</summary>
    private static Guid? ReadWorkflowId(string config)
    {
        try
        {
            return Guid.TryParse((string?)JsonNode.Parse(config)?[GenerationConfigKeys.WorkflowId], out var workflowId) ? workflowId : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static (int VariationCount, string AspectRatio) ReadConfig(string config)
    {
        try
        {
            var node = JsonNode.Parse(config);
            var variationCount = (int?)node?[GenerationConfigKeys.VariationCount] ?? GenerationConfigKeys.DefaultVariationCount;
            var aspectRatio = (string?)node?[GenerationConfigKeys.AspectRatio] ?? GenerationConfigKeys.DefaultAspectRatio;
            return (variationCount, aspectRatio);
        }
        catch (System.Text.Json.JsonException)
        {
            return (GenerationConfigKeys.DefaultVariationCount, GenerationConfigKeys.DefaultAspectRatio);
        }
    }

    private static string FileExtension(string? mimeType) => mimeType?.Split('/').LastOrDefault() ?? "png";

    /// <summary>
    /// Reads width/height straight out of a PNG header (bytes 16-23, big-endian) without a
    /// System.Drawing/ImageSharp dependency. Gemini's documented output format is PNG; any other
    /// format falls back to 0x0 rather than guessing.
    /// </summary>
    // A transparent PNG when the backdrop could be removed; otherwise the image exactly as generated.
    private (byte[] Bytes, string MimeType) CutOutBackground(byte[] bytes, string mimeType)
    {
        try
        {
            return backgroundRemover.RemoveBackground(bytes) is { } png ? (png, "image/png") : (bytes, mimeType);
        }
        catch (Exception)
        {
            // An image the processor can't read is still a valid design; keep it unchanged.
            return (bytes, mimeType);
        }
    }

    private static (int Width, int Height) TryReadPngDimensions(byte[] bytes)
    {
        const string pngSignature = "\x89PNG\r\n\x1a\n";
        if (bytes.Length < 24 || System.Text.Encoding.Latin1.GetString(bytes, 0, 8) != pngSignature)
            return (0, 0);

        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }
}
