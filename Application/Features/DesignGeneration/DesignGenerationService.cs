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
    IDesignGenerationQueue queue,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<StartGenerationRequestDto> validator) : IDesignGenerationService
{
    private const string GeminiProvider = "gemini";
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(3);

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
                Instructions = row.CustomInstructions ?? string.Empty
            };
            var generatedPrompt = PromptComposer.Compose(
                defaults.BasePrompt, effective.Subject, effective.ArtStyle, effective.MoodTone,
                effective.NegativeTerms, effective.Instructions, defaults.Niche, defaults.StyleModifiers);

            await aiPrompts.AddAsync(new AiPrompt
            {
                Id = Guid.NewGuid(),
                ProductId = row.ProductId!.Value,
                DesignTemplateId = product?.DesignTemplateId,
                OriginalDescription = product?.InputDescription ?? string.Empty,
                SystemPrompt = defaults.BasePrompt,
                GeneratedPrompt = generatedPrompt,
                VersionNumber = 1,
                BatchJobProductId = row.Id,
                CreatedAt = now
            }, cancellationToken: cancellationToken);

            row.Status = BatchJobProductStatuses.GeneratingImage;
            row.StartedAt = now;
            row.UpdatedAt = now;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        job.Status = BatchJobStatuses.Queued;
        job.Config = WriteConfig(job.Config, request.VariationCount, request.AspectRatio);
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
        var (variationCount, aspectRatio) = ReadConfig(job.Config);

        job.Status = BatchJobStatuses.Running;
        job.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var rows = await batchJobProducts.Query()
            .Where(x => x.BatchJobId == job.Id && x.Status == BatchJobProductStatuses.GeneratingImage)
            .OrderBy(x => x.SequenceOrder)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
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
                    result = await imageProvider.GenerateAsync(apiKeyPlain, prompt.GeneratedPrompt, aspectRatio, stepTimeout.Token);
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

                var storageKey = $"design-images/{prompt.Id}/{variation}";
                string imageUrl;
                using (var stream = new MemoryStream(result.ImageBytes!))
                {
                    try
                    {
                        imageUrl = await publicImages.UploadImageAsync(
                            new UploadFileDto($"{Guid.NewGuid()}.{FileExtension(result.MimeType)}", result.MimeType!, stream.Length, stream),
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

                var (width, height) = TryReadPngDimensions(result.ImageBytes!);
                await designImages.AddAsync(new DesignImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = row.ProductId!.Value,
                    AiPromptId = prompt.Id,
                    BatchJobId = job.Id,
                    BatchJobProductId = row.Id,
                    ApiUsageRecordId = usageRecord.Id,
                    ImageGeneratorModel = result.ModelUsed,
                    StorageProvider = "cloudinary",
                    StorageKey = storageKey,
                    ImageUrl = imageUrl,
                    ImageWidthPx = width,
                    ImageHeightPx = height,
                    FileFormat = FileExtension(result.MimeType),
                    FileSizeMb = Math.Round(result.ImageBytes!.Length / 1024m / 1024m, 3),
                    ApprovalStatus = "pending",
                    VariationIndex = variation,
                    GenerationTimeSeconds = elapsedSeconds,
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime
                }, cancellationToken: cancellationToken);

                successCount++;
            }

            row.Status = successCount > 0 ? BatchJobProductStatuses.ImageReviewRequired : BatchJobProductStatuses.Failed;
            row.ErrorMessage = successCount == 0 ? "All requested variations failed or were blocked." : null;
            row.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            row.UpdatedAt = row.CompletedAt;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);

            if (row.ProductId.HasValue)
            {
                var product = await products.Query().SingleOrDefaultAsync(p => p.Id == row.ProductId.Value, cancellationToken);
                if (product is not null)
                {
                    product.ProcessingStatus = successCount > 0 ? BatchJobProductStatuses.ImageReviewRequired : BatchJobProductStatuses.Failed;
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

    private async Task IncrementImagesGeneratedAsync(Guid userId, int count, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var usage = await usageStatistics.Query()
            .SingleOrDefaultAsync(u => u.UserId == userId && u.BillingPeriodStart <= today && u.BillingPeriodEnd >= today, cancellationToken);

        if (usage is null)
        {
            var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
            var periodEnd = subscription?.RenewalDate ?? today.AddMonths(1);
            usage = new UsageStatistic
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BillingPeriodStart = periodEnd.AddMonths(-1),
                BillingPeriodEnd = periodEnd,
                ImagesGenerated = count,
                UpdatedAt = now,
                CreatedAt = now
            };
            // A brand-new row must only be Added: calling UpdateAsync on it would make EF emit an
            // UPDATE for a row that does not exist yet ("affected 0 row(s)").
            await usageStatistics.AddAsync(usage, cancellationToken: cancellationToken);
            return;
        }

        usage.ImagesGenerated = (usage.ImagesGenerated ?? 0) + count;
        usage.UpdatedAt = now;
        await usageStatistics.UpdateAsync(usage, cancellationToken: cancellationToken);
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

    private static string WriteConfig(string config, int variationCount, string aspectRatio)
    {
        JsonObject root;
        try { root = JsonNode.Parse(config)?.AsObject() ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { root = new JsonObject(); }

        root[GenerationConfigKeys.VariationCount] = variationCount;
        root[GenerationConfigKeys.AspectRatio] = aspectRatio;
        return root.ToJsonString();
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
