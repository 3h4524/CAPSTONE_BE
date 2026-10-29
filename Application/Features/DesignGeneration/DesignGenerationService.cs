using System.Text.Json.Nodes;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.Batches.Validators;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Application.Features.DesignGeneration.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    IEnumerable<IImageGenerationProvider> imageProviders,
    IPublicImageService publicImages,
    IDesignBackgroundRemover backgroundRemover,
    IDesignGenerationQueue queue,
    IBatchJobClaims claims,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<StartGenerationRequestDto> validator,
    IOptions<DesignGenerationSettings> settings) : IDesignGenerationService
{
    // Products whose designs are printed as a standalone graphic on the item, not edge to edge.
    private static readonly HashSet<string> GraphicPrintTypes = new(StringComparer.OrdinalIgnoreCase) { "tshirt", "hoodie", "tote_bag" };

    // Sent with the prompt (not stored in it) so the result has a plain backdrop that can be removed.
    private const string GraphicPrintInstruction =
        "Output only the print-ready artwork, centered, on a plain solid white background. "
        + "No t-shirt, mockup, fabric, paper or photo backdrop, and no drop shadow around the artwork.";

    // The same, for a provider that leaves the background transparent itself: no backdrop is asked for.
    private const string TransparentGraphicPrintInstruction =
        "Output only the print-ready artwork, centered. No t-shirt, mockup, fabric, paper or photo backdrop.";

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

        // BR51: a valid, connected key for the chosen image provider must exist before the job can start.
        var imageProvider = request.ImageProvider ?? ImageGenerationProviders.Default;
        var ownedKeys = await apiKeys.ListOwnedAsync(userId, cancellationToken);
        if (!ownedKeys.Any(key => IsUsableKey(key, imageProvider, now)))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.MissingApiKey(imageProvider));

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
            // "No style" is a choice too: the style an earlier job gave the product must not stay on it.
            product.StylePreset = overrideStyleName;
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
            var promptProductId = row.ProductId!.Value;
            var promptVersion = latestVersionByProduct.GetValueOrDefault(promptProductId) + 1;
            latestVersionByProduct[promptProductId] = promptVersion;
            await AddPromptAsync(row, product, request.Instructions, promptVersion, now, cancellationToken);

            row.Status = BatchJobProductStatuses.GeneratingImage;
            row.StartedAt = now;
            row.UpdatedAt = now;
            await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        job.Status = BatchJobStatuses.Queued;
        job.Config = WriteConfig(job.Config, request.VariationCount, request.AspectRatio, request.Instructions, request.RequireApproval,
            request.WorkflowId, imageProvider);
        job.StartedAt = now;
        job.UpdatedAt = now;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);

        var batch = await batches.Query().SingleOrDefaultAsync(b => b.Id == job.BatchId, cancellationToken);
        if (batch is not null)
        {
            batch.Status = BatchStatuses.Processing;
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

        // BR51: a valid, connected key for the job's image provider must still exist.
        var imageProvider = ReadImageProvider(job.Config);
        var ownedKeys = await apiKeys.ListOwnedAsync(userId, cancellationToken);
        if (!ownedKeys.Any(key => IsUsableKey(key, imageProvider, now)))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.MissingApiKey(imageProvider));

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
            batch.Status = BatchStatuses.Processing;
            batch.UpdatedAt = now;
            await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        }

        await LogAsync(job.Id, null, "info", "retry_requested", $"Retrying {failedRows.Count} failed product(s).", cancellationToken);

        // Commit before enqueueing so the worker, which uses its own DbContext, sees the reset rows.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        queue.Enqueue(job.Id);

        return Result.Success(new StartGenerationResponseDto(job.Id, failedRows.Count, job.Status));
    }

    public async Task<Result<StartGenerationResponseDto>> RegenerateProductAsync(
        Guid batchJobId,
        Guid batchJobProductId,
        RegenerateProductRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.Unauthenticated("regenerate"));
        if (request.CustomPrompt?.Trim().Length > ProductRules.MaximumCustomPromptLength)
            return Result.Failure<StartGenerationResponseDto>(Error.Validation(
                $"Keep the image prompt under {ProductRules.MaximumCustomPromptLength} characters."));
        if (request.DesignText?.Trim().Length > ProductRules.MaximumDesignTextLength)
            return Result.Failure<StartGenerationResponseDto>(Error.Validation(
                $"Keep the design text under {ProductRules.MaximumDesignTextLength} characters."));
        if (request.VariationCount is < GenerationConfigKeys.MinVariationCount or > GenerationConfigKeys.MaxVariationCount)
            return Result.Failure<StartGenerationResponseDto>(Error.Validation(
                $"Generate between {GenerationConfigKeys.MinVariationCount} and {GenerationConfigKeys.MaxVariationCount} designs."));
        if (request.Instructions?.Trim().Length > PromptRules.MaximumInstructionsLength)
            return Result.Failure<StartGenerationResponseDto>(Error.Validation(
                $"Keep the instructions under {PromptRules.MaximumInstructionsLength} characters."));
        Guid? stylePresetId = null;
        if (!string.IsNullOrWhiteSpace(request.StyleArtPresetId))
        {
            if (!Guid.TryParse(request.StyleArtPresetId, out var parsed))
                return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.StyleNotFound());
            stylePresetId = parsed;
        }

        var job = await batchJobs.Query()
            .SingleOrDefaultAsync(x => x.Id == batchJobId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (job is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.JobNotFound());
        if (job.Status is not (BatchJobStatuses.Failed or BatchJobStatuses.PartiallyCompleted or BatchJobStatuses.Completed))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NotRegeneratable());

        // BR52: no other active job for the same batch.
        var hasActiveJob = await batchJobs.Query().AnyAsync(x => x.BatchId == job.BatchId && x.Id != job.Id
            && x.DeletedAt == null && BatchJobStatuses.Active.Contains(x.Status.ToLower()), cancellationToken);
        if (hasActiveJob) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.ActiveJobExists());

        var row = await batchJobProducts.Query()
            .SingleOrDefaultAsync(x => x.Id == batchJobProductId && x.BatchJobId == job.Id, cancellationToken);
        if (row is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.RowNotFound());
        if (row.Status is not (BatchJobProductStatuses.ImageReviewRequired or BatchJobProductStatuses.Approved or BatchJobProductStatuses.Failed))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NotRegeneratable());
        var product = row.ProductId is Guid productId
            ? await products.Query().SingleOrDefaultAsync(p => p.Id == productId && p.DeletedAt == null, cancellationToken)
            : null;
        if (product is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.ProductNotFound());

        // A product that was set back to pending, or already put into a later job, is generated there instead.
        var takenOver = await batchJobProducts.Query().AnyAsync(other => other.BatchJobId != job.Id && other.ProductId == product.Id
            && other.CreatedAt > job.CreatedAt, cancellationToken);
        if (takenOver || product.ProcessingStatus == BatchJobProductStatuses.Pending)
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.ProductMovedOn());

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // BR51: a valid, connected key for the job's image provider must still exist.
        var imageProvider = ReadImageProvider(job.Config);
        var ownedKeys = await apiKeys.ListOwnedAsync(userId, cancellationToken);
        if (!ownedKeys.Any(key => IsUsableKey(key, imageProvider, now)))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.MissingApiKey(imageProvider));

        // BR50: generating again consumes quota again.
        var subscription = await subscriptions.GetActiveWithPlanAsync(userId, cancellationToken);
        if (subscription is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.NoActivePlan());
        // The Seller may ask for a different number of designs than the job first made.
        var variationCount = request.VariationCount ?? ReadConfig(job.Config).VariationCount;
        var usage = await usageStatistics.GetCurrentPeriodAsync(userId, DateOnly.FromDateTime(now), cancellationToken);
        if (variationCount > subscription.Plan.ImageGenerationQuota - (usage?.ImagesGenerated ?? 0))
            return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.InsufficientQuota());

        // A template or art style chosen here is the product's from now on, as the workflow's choice is when a job starts.
        if (request.DesignTemplateId is Guid templateId)
        {
            if (!await designTemplates.Query().AnyAsync(t => t.Id == templateId, cancellationToken))
                return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.TemplateNotFound());
            product.DesignTemplateId = templateId;
        }
        if (request.StyleArtPresetId is not null)
        {
            string? styleName = null;
            if (stylePresetId is Guid presetId)
            {
                styleName = await styleArtPresets.Query().Where(s => s.Id == presetId).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken);
                if (styleName is null) return Result.Failure<StartGenerationResponseDto>(DesignGenerationErrors.StyleNotFound());
            }
            product.StylePreset = styleName;
            // An art style typed in the prompt editor would take the place of the one just chosen.
            row.CustomArtStyle = null;
        }

        // This product's instructions in this job, as the prompt editor sets them: empty means none, rather than the job's.
        if (request.Instructions is not null) row.CustomInstructions = request.Instructions.Trim();

        // The Seller's changes are the product's, so a later job makes its designs from them too.
        if (request.CustomPrompt is not null) product.CustomPrompt = PromptComposer.NullIfEmpty(request.CustomPrompt);
        if (request.DesignText is not null)
        {
            product.DesiredDesignText = PromptComposer.NullIfEmpty(request.DesignText);
            // A subject changed in the prompt editor would print the old words instead of the new design text.
            row.CustomSubject = null;
        }
        product.ProcessingStatus = BatchJobProductStatuses.Queued;
        product.UpdatedAt = now;
        await products.UpdateAsync(product, cancellationToken: cancellationToken);

        // A new prompt version, made from the product as it is now; the earlier designs keep the prompt they came from.
        var latestVersion = await aiPrompts.Query()
            .Where(p => p.ProductId == product.Id)
            .Select(p => (int?)p.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;
        await AddPromptAsync(row, product, ReadInstructions(job.Config), latestVersion + 1, now, cancellationToken);

        var wasFailed = row.Status == BatchJobProductStatuses.Failed;
        row.Status = BatchJobProductStatuses.GeneratingImage;
        row.ErrorMessage = null;
        row.StartedAt = now;
        row.CompletedAt = null;
        row.UpdatedAt = now;
        await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);

        var total = job.TotalProducts ?? 1;
        job.ProcessedProducts = Math.Max(0, (job.ProcessedProducts ?? 0) - 1);
        if (wasFailed) job.FailedProducts = Math.Max(0, (job.FailedProducts ?? 0) - 1);
        job.ProgressPercentage = total > 0 ? Math.Round(100m * job.ProcessedProducts.Value / total, 0) : 0;
        job.Status = BatchJobStatuses.Queued;
        job.CompletedAt = null;
        job.UpdatedAt = now;
        job.Config = WriteRegenerate(job.Config, row.Id, variationCount);
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);

        var batch = await batches.Query().SingleOrDefaultAsync(b => b.Id == job.BatchId, cancellationToken);
        if (batch is not null)
        {
            batch.Status = BatchStatuses.Processing;
            batch.UpdatedAt = now;
            await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        }

        await LogAsync(job.Id, row.Id, "info", "regenerate_requested", $"Generating \"{product.Name}\" again.", cancellationToken);

        // Commit before enqueueing so the worker, which uses its own DbContext, sees the new prompt and the reset row.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        queue.Enqueue(job.Id);

        return Result.Success(new StartGenerationResponseDto(job.Id, 1, job.Status));
    }

    public async Task ProcessBatchJobAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        var job = await batchJobs.Query().SingleOrDefaultAsync(x => x.Id == batchJobId, cancellationToken);
        if (job is null) return;

        var providerName = ReadImageProvider(job.Config);
        var imageProvider = imageProviders.FirstOrDefault(provider => provider.Provider == providerName);
        if (imageProvider is null)
        {
            await FailJobAsync(job.Id, $"Image generation with {ImageGenerationProviders.Label(providerName)} is not available.", cancellationToken);
            return;
        }
        var ownedKeys = await apiKeys.ListOwnedAsync(job.UserId, cancellationToken);
        var providerKey = ownedKeys.FirstOrDefault(key =>
            string.Equals(key.ServiceProvider, providerName, StringComparison.OrdinalIgnoreCase) && key.IsActive == true);
        if (providerKey is null)
        {
            // Defensive only: StartAsync already required a valid key moments earlier. The job is failed rather
            // than left queued, where nothing would ever finish it.
            await FailJobAsync(job.Id, $"The {ImageGenerationProviders.Label(providerName)} API key is no longer available.", cancellationToken);
            return;
        }
        var apiKeyPlain = apiKeyCredentials.Unprotect(providerKey.KeyValueEncrypted);
        var requireApproval = ReadRequireApproval(job.Config);
        // Automatic approval marks the images and the product approved as they are made; manual leaves them for the Seller.
        var reviewedStatus = requireApproval == false ? BatchJobProductStatuses.Approved : BatchJobProductStatuses.ImageReviewRequired;
        var (variationCount, aspectRatio) = ReadConfig(job.Config);
        var regenerate = ReadRegenerate(job.Config);

        // The same job can be waiting in the queue of more than one application instance. The claim is made
        // in the database, so only one of them goes on; it also marks the job as running.
        if (!await claims.TryClaimAsync(job.Id, cancellationToken))
            return;
        job.Status = BatchJobStatuses.Running;
        job.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        var rows = await batchJobProducts.Query()
            .Where(x => x.BatchJobId == job.Id && x.Status == BatchJobProductStatuses.GeneratingImage)
            .OrderBy(x => x.SequenceOrder)
            .ToListAsync(cancellationToken);

        // Read up front, so the products processed side by side below never have to ask the database for them.
        var rowIds = rows.Select(row => row.Id).ToList();
        var promptByRow = (await aiPrompts.Query()
                .Where(p => p.BatchJobProductId != null && rowIds.Contains(p.BatchJobProductId!.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.BatchJobProductId!.Value)
            .ToDictionary(group => group.Key, group => group.MaxBy(p => p.VersionNumber)!);
        var rowProductIds = rows.Where(row => row.ProductId.HasValue).Select(row => row.ProductId!.Value).ToList();
        var productById = await products.Query()
            .Where(p => rowProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var templateIds = promptByRow.Values.Where(p => p.DesignTemplateId.HasValue).Select(p => p.DesignTemplateId!.Value).Distinct().ToList();
        var templateById = await designTemplates.Query()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var historyByRow = (await designImages.Query()
                .Where(image => image.BatchJobProductId != null && rowIds.Contains(image.BatchJobProductId!.Value) && image.DeletedAt == null)
                .Select(image => new { RowId = image.BatchJobProductId!.Value, image.VariationIndex, image.ApprovalStatus })
                .ToListAsync(cancellationToken))
            .GroupBy(image => image.RowId)
            .ToDictionary(group => group.Key, group => new RowHistory(
                group.Max(image => image.VariationIndex),
                group.Any(image => image.ApprovalStatus == ApprovalStatuses.Approved)));
        // Ideogram fills out itself the prompt of a product the Seller gave next to nothing for.
        var thinRows = imageProvider.Provider == ImageGenerationProviders.Ideogram
            ? await ThinRowsAsync(job, rows, productById, cancellationToken)
            : [];

        // Images are mostly waiting on the provider, so several are requested at once, up to what the Seller's key allows.
        var run = new JobRun(job, imageProvider, apiKeyPlain, aspectRatio, variationCount, requireApproval, reviewedStatus, new JobStop());
        var maxImages = settings.Value.MaxConcurrentImagesFor(imageProvider.Provider);
        using var imageSlots = new SemaphoreSlim(maxImages);
        // Just enough products at once to keep every slot busy, so the products shown as being generated are the
        // ones whose images are being made, still taken in their order.
        var productsAtOnce = Math.Max(1, (maxImages + variationCount - 1) / Math.Max(1, variationCount));

        // Once the user cancels, the products being generated finish and every one after them is failed.
        var cancelled = false;
        await Parallel.ForEachAsync(rows,
            new ParallelOptions { MaxDegreeOfParallelism = productsAtOnce, CancellationToken = cancellationToken },
            async (row, token) =>
            {
                var skip = await WithDbAsync(async () =>
                {
                    cancelled = cancelled || await CancelPendingAsync(job.Id, token);
                    if (cancelled) await CancelRowAsync(job, row, token);
                    return cancelled;
                }, token);
                if (skip) return;

                var product = row.ProductId is Guid productId ? productById.GetValueOrDefault(productId) : null;
                var prompt = promptByRow.GetValueOrDefault(row.Id);
                var template = prompt?.DesignTemplateId is Guid templateId ? templateById.GetValueOrDefault(templateId) : null;
                // A product generated again makes as many designs as the Seller asked for then.
                var designs = regenerate is { } again && again.RowId == row.Id ? again.VariationCount : variationCount;
                await ProcessRowAsync(run, row, prompt, product, template, thinRows.Contains(row.Id), historyByRow.GetValueOrDefault(row.Id),
                    designs, imageSlots, token);
            });

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
            batch.Status = BatchStatuses.Completed;
            batch.UpdatedAt = job.CompletedAt;
            await batches.UpdateAsync(batch, cancellationToken: cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // Synthesizes the effective prompt of one product from its template or own prompt and the Seller's changes
    // (SRS 3.5.10), and stores it for the row as the product's next prompt version.
    private async Task AddPromptAsync(BatchJobProduct row, Product? product, string? jobInstructions, int version, DateTime now,
        CancellationToken cancellationToken)
    {
        var defaults = await PromptDefaultsResolver.ResolveAsync(product, designTemplates, styleArtPresets, cancellationToken);
        var effective = EffectiveComponents(defaults, row, jobInstructions);
        var generatedPrompt = PromptComposer.Compose(
            defaults.BasePrompt, effective.Subject, effective.ArtStyle, effective.MoodTone,
            effective.NegativeTerms, effective.Instructions, defaults.Niche, defaults.StyleModifiers,
            effective.Keywords, effective.Description);

        await aiPrompts.AddAsync(new AiPrompt
        {
            Id = Guid.NewGuid(),
            ProductId = row.ProductId!.Value,
            DesignTemplateId = product?.DesignTemplateId,
            OriginalDescription = product?.InputDescription ?? string.Empty,
            SystemPrompt = defaults.BasePrompt,
            GeneratedPrompt = generatedPrompt,
            VersionNumber = version,
            BatchJobProductId = row.Id,
            CreatedAt = now
        }, cancellationToken: cancellationToken);
    }

    // The template's defaults with the Seller's own changes to this product, fitted to the phrase it prints: which motifs
    // and description words repeat the phrase depends on the phrase.
    private static PromptComponents EffectiveComponents(PromptComponents defaults, BatchJobProduct row, string? jobInstructions) =>
        ArtworkPromptRules.Fit(WithSellerChanges(defaults, row, jobInstructions));

    // The product carries the Seller's own image prompt, which replaces the design template.
    private static bool HasCustomPrompt(Product? product) => !string.IsNullOrWhiteSpace(product?.CustomPrompt);

    private static PromptComponents WithSellerChanges(PromptComponents defaults, BatchJobProduct row, string? jobInstructions) =>
        defaults with
        {
            Subject = row.CustomSubject ?? defaults.Subject,
            // The product's own image prompt sets the look, so no art style is added to it.
            ArtStyle = defaults.OwnPrompt ? string.Empty : row.CustomArtStyle ?? defaults.ArtStyle,
            MoodTone = row.CustomMoodTone ?? defaults.MoodTone,
            NegativeTerms = row.CustomNegativeTerms ?? string.Empty,
            // The job-level wording applies to products that have no instructions of their own.
            Instructions = row.CustomInstructions ?? jobInstructions?.Trim() ?? string.Empty
        };

    /// <summary>
    /// The rows whose product the Seller gave next to nothing to design from (<see cref="ArtworkPromptRules.IsThin"/>),
    /// worked out from the same pieces the stored prompt was made of, read again now (the template, the product and the
    /// Seller's changes).
    /// </summary>
    private async Task<HashSet<Guid>> ThinRowsAsync(BatchJob job, IEnumerable<BatchJobProduct> rows,
        IReadOnlyDictionary<Guid, Product> productById, CancellationToken cancellationToken)
    {
        var thin = new HashSet<Guid>();
        var jobInstructions = ReadInstructions(job.Config);
        foreach (var row in rows)
        {
            if (row.ProductId is not Guid productId || !productById.TryGetValue(productId, out var product)) continue;
            // The Seller's own prompt is all there is to design from: it is thin when it is only a few words.
            if (HasCustomPrompt(product))
            {
                // Instructions added to it describe the design as well.
                var instructions = row.CustomInstructions ?? jobInstructions ?? string.Empty;
                if (ArtworkPromptRules.IsThinCustomPrompt($"{product.CustomPrompt} {instructions}")) thin.Add(row.Id);
                continue;
            }
            var defaults = await PromptDefaultsResolver.ResolveAsync(product, designTemplates, styleArtPresets, cancellationToken);
            if (ArtworkPromptRules.IsThin(WithSellerChanges(defaults, row, jobInstructions))) thin.Add(row.Id);
        }
        return thin;
    }

    // What every product of a job being processed shares.
    private sealed record JobRun(BatchJob Job, IImageGenerationProvider ImageProvider, string ApiKey, string AspectRatio, int VariationCount,
        bool? RequireApproval, string ReviewedStatus, JobStop Stop);

    // The designs a product already has in the job when it is generated again: the new ones are numbered after them,
    // and the product keeps them, so it does not fail when none of the new ones can be made.
    private sealed record RowHistory(int LastVariation, bool HasApproved);

    // Set once the provider turns the account away (no credits, a refused key): the job asks for no further image.
    private sealed class JobStop
    {
        private string? reason;

        public string? Reason => Volatile.Read(ref reason);

        // True for the image that stopped the job; the ones that meet the same refusal later change nothing.
        public bool TrySet(string reason) => Interlocked.CompareExchange(ref this.reason, reason, null) is null;
    }

    // Every repository here shares one DbContext, which two images must never use at the same moment: while a
    // job's images are made side by side, each read or write of it waits its turn here.
    private readonly SemaphoreSlim dbGate = new(1, 1);

    private async Task<T> WithDbAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await dbGate.WaitAsync(cancellationToken);
        try
        {
            return await work();
        }
        finally
        {
            dbGate.Release();
        }
    }

    private Task WithDbAsync(Func<Task> work, CancellationToken cancellationToken) =>
        WithDbAsync(async () =>
        {
            await work();
            return true;
        }, cancellationToken);

    // Makes every variation of one product, side by side, then records how the product came out.
    private async Task ProcessRowAsync(JobRun run, BatchJobProduct row, AiPrompt? prompt, Product? product, DesignTemplate? template,
        bool enrich, RowHistory? history, int variationCount, SemaphoreSlim imageSlots, CancellationToken cancellationToken)
    {
        var job = run.Job;
        if (prompt is null)
        {
            await WithDbAsync(async () =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                row.Status = BatchJobProductStatuses.Failed;
                row.ErrorMessage = "No synthesized prompt was found for this product.";
                row.CompletedAt = now;
                row.UpdatedAt = now;
                await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
                job.FailedProducts = (job.FailedProducts ?? 0) + 1;
                job.ProcessedProducts = (job.ProcessedProducts ?? 0) + 1;
                await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
            return;
        }

        // A graphic printed on its own needs no backdrop: a provider that can leave it transparent is asked to, any
        // other is asked for a plain white one that is cut out afterwards.
        var graphicPrint = product is not null && GraphicPrintTypes.Contains(product.ProductType);
        var transparent = graphicPrint && run.ImageProvider.SupportsTransparentBackground;
        var cutOut = graphicPrint && !transparent;
        if (enrich)
            await WithDbAsync(() => LogAsync(run.Job.Id, row.Id, "info", "prompt_enriched",
                "The product has little detail, so the provider fills out the prompt itself.", cancellationToken), cancellationToken);
        // Which lettering belongs in the artwork is decided by the template the prompt was made from, so prompts
        // stored before this rule existed get it too when they are retried.
        var instructions = new List<string>
        {
            prompt.GeneratedPrompt,
            ArtworkPromptRules.LetteringInstruction(prompt.SystemPrompt, graphicPrint, ownPrompt: HasCustomPrompt(product)),
            ArtworkPromptRules.UnbrandedInstruction
        };
        // The template's negative terms, unless the Seller wrote their own: those are already in the stored prompt. A
        // design made from the Seller's own prompt has no template to take them from, so it avoids the usual faults.
        var avoid = HasCustomPrompt(product)
            ? ArtworkPromptRules.CustomAvoidInstruction(prompt.SystemPrompt)
            : ArtworkPromptRules.AvoidInstruction(template?.NegativePrompt);
        if (row.CustomNegativeTerms is null && avoid.Length > 0)
            instructions.Add(avoid);
        if (graphicPrint) instructions.Add(transparent ? TransparentGraphicPrintInstruction : GraphicPrintInstruction);
        var requestPrompt = string.Join("\n", instructions);

        // Until now the product was only waiting its turn. Saved with the log entry of its first image.
        if (product is not null)
        {
            await WithDbAsync(async () =>
            {
                product.ProcessingStatus = BatchJobProductStatuses.GeneratingImage;
                product.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
                await products.UpdateAsync(product, cancellationToken: cancellationToken);
            }, cancellationToken);
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, variationCount).Select(variation =>
            GenerateVariationAsync(run, row, prompt, requestPrompt, enrich, transparent, cutOut, variation, variationCount,
                history?.LastVariation ?? 0, imageSlots, cancellationToken)));

        await WithDbAsync(() => CompleteRowAsync(run, row, product, outcomes, history, cancellationToken), cancellationToken);
    }

    // How one requested design came out: saved, or not made, with the provider's reason when it blocked it (a safety or
    // copyright check), which the Seller is shown.
    private sealed record VariationOutcome(bool Saved, string? BlockedReason = null)
    {
        public static readonly VariationOutcome Made = new(true);
        public static readonly VariationOutcome Lost = new(false);
    }

    /// <summary>
    /// Makes one image once a slot is free and saves it, and says how it went. Only the provider call, the
    /// cut-out and the upload run alongside other images; everything written goes through WithDbAsync.
    /// </summary>
    /// <param name="variationCount">How many designs the product is getting this time.</param>
    /// <param name="lastVariation">The highest variation number the product already has in the job; the new one follows it.</param>
    private async Task<VariationOutcome> GenerateVariationAsync(JobRun run, BatchJobProduct row, AiPrompt prompt, string requestPrompt,
        bool enrich, bool transparent, bool cutOut, int variation, int variationCount, int lastVariation, SemaphoreSlim imageSlots,
        CancellationToken cancellationToken)
    {
        var job = run.Job;

        await imageSlots.WaitAsync(cancellationToken);
        try
        {
            // The provider already turned the account away: asking again would only be refused again.
            if (run.Stop.Reason is not null) return VariationOutcome.Lost;

            // Saved with the log entry: the job's update time tells other workers it is still being worked on.
            await WithDbAsync(async () =>
            {
                job.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
                await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);
                await LogAsync(job.Id, row.Id, "info", "generation_started",
                    $"Requesting image {variation + 1}/{variationCount} from the provider.", cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            var started = timeProvider.GetUtcNow();

            // Hard cap for the provider's part of one image (the request, any waiting for it and its download), so
            // nothing can hang the job forever. Started only now: the time spent waiting for a slot does not count.
            using var providerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            providerTimeout.CancelAfter(settings.Value.ProviderTimeout);

            ImageGenerationResult result;
            try
            {
                result = await run.ImageProvider.GenerateAsync(run.ApiKey, requestPrompt, run.AspectRatio, transparent, enrich, providerTimeout.Token);
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
                var stopsJob = result.StopsJob && run.Stop.TrySet(result.ErrorMessage ?? "The provider refused the account.");
                await WithDbAsync(async () =>
                {
                    await LogAsync(job.Id, row.Id, "warning",
                        result.IsBlocked ? "generation_blocked" : "generation_failed",
                        result.ErrorMessage ?? "Unknown error", cancellationToken);
                    if (stopsJob)
                        await LogAsync(job.Id, null, "error", "generation_stopped",
                            $"{ImageGenerationProviders.Label(run.ImageProvider.Provider)} refused the account, so no further image is requested: "
                            + run.Stop.Reason, cancellationToken);
                }, cancellationToken);
                return result.IsBlocked
                    ? new VariationOutcome(false, result.ErrorMessage ?? "The provider blocked the image.")
                    : VariationOutcome.Lost;
            }

            var cutOutStarted = timeProvider.GetTimestamp();
            var (imageBytes, mimeType) = cutOut
                ? CutOutBackground(result.ImageBytes!, result.MimeType!)
                : (result.ImageBytes!, result.MimeType!);
            var cutOutTime = timeProvider.GetElapsedTime(cutOutStarted);

            // Keyed by the image's own id (created before upload) so each file maps 1:1 to its row
            // and a later regenerate with the same prompt can never overwrite an existing image.
            var designImageId = Guid.NewGuid();
            var storageKey = $"design-images/{designImageId:N}";
            PublicImageUploadResult uploaded;
            var uploadStarted = timeProvider.GetTimestamp();
            using var uploadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            uploadTimeout.CancelAfter(settings.Value.UploadTimeout);
            using (var stream = new MemoryStream(imageBytes))
            {
                try
                {
                    // Gemini returns JPEG, so the stored size comes from the upload rather than the bytes.
                    uploaded = await publicImages.UploadImageWithMetadataAsync(
                        new UploadFileDto($"{Guid.NewGuid()}.{FileExtension(mimeType)}", mimeType, stream.Length, stream),
                        storageKey,
                        uploadTimeout.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    await WithDbAsync(() => LogAsync(job.Id, row.Id, "error", "upload_failed",
                        ex is OperationCanceledException ? "Image upload timed out." : ex.Message, cancellationToken), cancellationToken);
                    return VariationOutcome.Lost;
                }
            }
            var uploadTime = timeProvider.GetElapsedTime(uploadStarted);

            // Saved right away, so the image shows up while the others are still being made.
            await WithDbAsync(async () =>
            {
                var usageRecord = new ApiUsageRecord
                {
                    Id = Guid.NewGuid(),
                    UserId = job.UserId,
                    BatchJobId = job.Id,
                    ProductId = row.ProductId,
                    Provider = run.ImageProvider.Provider,
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
                    ApprovalStatus = run.RequireApproval == false ? ApprovalStatuses.Approved : ApprovalStatuses.Pending,
                    // 1-based: the column's DB default is 1, so EF drops a 0 and the DB stores 1 instead. A product generated
                    // again numbers its new designs after the ones it keeps.
                    VariationIndex = lastVariation + variation + 1,
                    GenerationTimeSeconds = elapsedSeconds,
                    GenerationMetadata = GenerationMetadata(result),
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime
                }, cancellationToken: cancellationToken);
                await LogTimingAsync(job.Id, row.Id, variation + 1, variationCount,
                    TimeSpan.FromSeconds((double)elapsedSeconds), result, cutOutTime, uploadTime, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
            return VariationOutcome.Made;
        }
        finally
        {
            imageSlots.Release();
        }
    }

    // Records how one product came out once all its variations are done. Called through WithDbAsync.
    private async Task CompleteRowAsync(JobRun run, BatchJobProduct row, Product? product, IReadOnlyList<VariationOutcome> outcomes,
        RowHistory? history, CancellationToken cancellationToken)
    {
        var job = run.Job;
        var successCount = outcomes.Count(outcome => outcome.Saved);
        // A product generated again keeps its earlier designs, so it only fails when it has none at all; it counts as
        // approved while one of its designs is (SetImageApprovalAsync's rule), the new ones being approved automatically
        // or left for review.
        var hasDesigns = successCount > 0 || history is not null;
        var hasApproved = history?.HasApproved == true || (successCount > 0 && run.ReviewedStatus == BatchJobProductStatuses.Approved);
        var status = !hasDesigns ? BatchJobProductStatuses.Failed
            : hasApproved ? BatchJobProductStatuses.Approved
            : BatchJobProductStatuses.ImageReviewRequired;
        row.Status = status;
        row.ErrorMessage = RowMessage(outcomes, run.Stop.Reason, keptEarlier: history is not null);
        row.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        row.UpdatedAt = row.CompletedAt;
        await batchJobProducts.UpdateAsync(row, cancellationToken: cancellationToken);
        if (successCount == 0 && history is not null)
            await LogAsync(job.Id, row.Id, "warning", "regenerate_failed",
                run.Stop.Reason ?? "No new design could be made; the earlier ones are kept.", cancellationToken);

        if (product is not null)
        {
            product.ProcessingStatus = status;
            product.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            await products.UpdateAsync(product, cancellationToken: cancellationToken);
        }

        if (successCount > 0) await IncrementImagesGeneratedAsync(job.UserId, successCount, cancellationToken);

        job.ProcessedProducts = (job.ProcessedProducts ?? 0) + 1;
        if (!hasDesigns) job.FailedProducts = (job.FailedProducts ?? 0) + 1;
        job.ProgressPercentage = job.TotalProducts is > 0
            ? Math.Round(100m * job.ProcessedProducts.Value / job.TotalProducts.Value, 0)
            : job.ProgressPercentage;
        job.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batchJobs.UpdateAsync(job, cancellationToken: cancellationToken);

        // Persist per-row: partial progress survives even if a later row throws or the process stops.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// What the Seller is told about a product once its designs are done: why it has none, or how many the provider
    /// blocked (a safety or copyright check, such as a real brand or character), so they can change the prompt. Null when
    /// every design was made, or when the only ones lost simply failed and the product kept earlier designs.
    /// </summary>
    private static string? RowMessage(IReadOnlyList<VariationOutcome> outcomes, string? stopReason, bool keptEarlier)
    {
        var requested = outcomes.Count;
        var saved = outcomes.Count(outcome => outcome.Saved);
        var blocked = outcomes.Where(outcome => outcome.BlockedReason is not null).ToList();
        var reason = blocked.Count > 0 ? Sentence(blocked[0].BlockedReason!) : null;

        if (saved == 0 && !keptEarlier)
        {
            // A product left without images because the provider refused the account says why, rather than the generic reason.
            if (stopReason is not null) return stopReason;
            if (reason is null) return "All requested variations failed or were blocked.";
            return blocked.Count == requested
                ? $"{reason} Change the prompt and generate it again."
                : $"No design could be made: {blocked.Count} of {requested} were blocked. {reason}";
        }
        if (reason is null) return null;

        var designs = $"{blocked.Count} of {requested} {(keptEarlier ? "new " : string.Empty)}design{(requested == 1 ? string.Empty : "s")}";
        return $"{designs} {(blocked.Count == 1 ? "was" : "were")} blocked. {reason}";
    }

    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?') ? trimmed : $"{trimmed}.";
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
            .Select(p => new { p.Id, p.Name, p.ProductType, p.DesiredDesignText, p.CustomPrompt, p.DesignTemplateId })
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
                imagesByRow.TryGetValue(r.Id, out var list) ? list : [],
                info?.DesiredDesignText, info?.CustomPrompt, info?.DesignTemplateId, r.CustomInstructions);
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
        TimeSpan provider, ImageGenerationResult result, TimeSpan cutOut, TimeSpan upload, CancellationToken cancellationToken)
    {
        var details = new JsonObject
        {
            ["providerMs"] = (long)provider.TotalMilliseconds,
            ["cutOutMs"] = (long)cutOut.TotalMilliseconds,
            ["uploadMs"] = (long)upload.TotalMilliseconds,
        };
        // A provider that answers with a link reports making the image and downloading it apart.
        var providerParts = string.Empty;
        if (result.GenerateTime is TimeSpan generate && result.DownloadTime is TimeSpan download)
        {
            details["generateMs"] = (long)generate.TotalMilliseconds;
            details["downloadMs"] = (long)download.TotalMilliseconds;
            providerParts = FormattableString.Invariant($" (generate {generate.TotalSeconds:0.0}s, download {download.TotalSeconds:0.0}s)");
        }

        await batchJobLogs.AddAsync(new BatchJobLog
        {
            Id = Guid.NewGuid(),
            BatchJobId = batchJobId,
            BatchJobProductId = batchJobProductId,
            LogLevel = "debug",
            EventType = ImageTimingEvent,
            Message = FormattableString.Invariant(
                $"Image {variation}/{variationCount}: provider {provider.TotalSeconds:0.0}s{providerParts}, cut-out {cutOut.TotalSeconds:0.0}s, upload {upload.TotalSeconds:0.0}s."),
            Details = details.ToJsonString(),
            DurationMs = (int)(provider + cutOut + upload).TotalMilliseconds,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken: cancellationToken);
    }

    // What the provider reports about how it made the image (its seed, and the prompt it drew from when it rewrote
    // the one it was sent), kept with the image so it can be looked into or made again; null when it reports nothing.
    private static string? GenerationMetadata(ImageGenerationResult result)
    {
        if (result.Seed is null && result.ProviderPrompt is null) return null;
        var metadata = new JsonObject();
        if (result.Seed is long seed) metadata["seed"] = seed;
        if (result.ProviderPrompt is { } providerPrompt) metadata["providerPrompt"] = providerPrompt;
        return metadata.ToJsonString();
    }

    // Connected, checked and not expired: a key a job can be started with.
    private static bool IsUsableKey(ApiKey key, string provider, DateTime now) =>
        string.Equals(key.ServiceProvider, provider, StringComparison.OrdinalIgnoreCase)
        && key.IsActive == true && key.LastCheckSucceeded == true
        && (!key.ExpiresAt.HasValue || key.ExpiresAt > now);

    private static string WriteConfig(string config, int variationCount, string aspectRatio, string? instructions, bool? requireApproval,
        Guid? workflowId, string imageProvider)
    {
        JsonObject root;
        try { root = JsonNode.Parse(config)?.AsObject() ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { root = new JsonObject(); }

        root[GenerationConfigKeys.VariationCount] = variationCount;
        root[GenerationConfigKeys.AspectRatio] = aspectRatio;
        root[GenerationConfigKeys.ImageProvider] = imageProvider;
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

    /// <summary>The job-level wording for products that have none of their own: null when the job has none.</summary>
    private static string? ReadInstructions(string config)
    {
        try
        {
            return (string?)JsonNode.Parse(config)?[GenerationConfigKeys.Instructions];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The provider the job's images are made with: Gemini for jobs started before there was a choice.</summary>
    private static string ReadImageProvider(string config)
    {
        try
        {
            return (string?)JsonNode.Parse(config)?[GenerationConfigKeys.ImageProvider] is { Length: > 0 } provider
                ? provider
                : ImageGenerationProviders.Default;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return ImageGenerationProviders.Default;
        }
    }

    // The one product being generated again and how many designs it gets: a product is only regenerated once its job
    // has finished, so there is never more than one at a time.
    private sealed record RegenerateRequest(Guid RowId, int VariationCount);

    private static string WriteRegenerate(string config, Guid rowId, int variationCount)
    {
        JsonObject root;
        try { root = JsonNode.Parse(config)?.AsObject() ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { root = new JsonObject(); }

        root[GenerationConfigKeys.Regenerate] = new JsonObject
        {
            [GenerationConfigKeys.RegenerateRowId] = rowId.ToString(),
            [GenerationConfigKeys.VariationCount] = variationCount
        };
        return root.ToJsonString();
    }

    private static RegenerateRequest? ReadRegenerate(string config)
    {
        try
        {
            var node = JsonNode.Parse(config)?[GenerationConfigKeys.Regenerate];
            return Guid.TryParse((string?)node?[GenerationConfigKeys.RegenerateRowId], out var rowId)
                && (int?)node?[GenerationConfigKeys.VariationCount] is int count
                && count is >= GenerationConfigKeys.MinVariationCount and <= GenerationConfigKeys.MaxVariationCount
                ? new RegenerateRequest(rowId, count)
                : null;
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
