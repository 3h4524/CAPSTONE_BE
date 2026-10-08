using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.BatchMockups;

/// <summary>Lists mock-up templates, stores per-batch mock-up selection, and composites mock-up images.</summary>
public sealed class MockupTemplateService(
    ICurrentUser currentUser,
    IRepository<MockupTemplate> templates,
    IRepository<BatchJob> batches,
    IRepository<BatchJobProduct> rows,
    IRepository<Product> products,
    IRepository<DesignImage> designImages,
    IRepository<MockupImage> mockupImages,
    IPublicImageService images,
    IMockupCompositor compositor,
    IMockupMapService mapService,
    IUnitOfWork unitOfWork,
    IValidator<ApplyMockupTemplatesRequestDto> applyValidator,
    IValidator<CreateMockupTemplateRequestDto> createValidator,
    IValidator<UpdateMockupTemplateRequestDto> updateValidator,
    IValidator<GenerateMockupImageRequestDto> generateValidator,
    TimeProvider timeProvider,
    ILogger<MockupTemplateService> logger) : IMockupTemplateService
{
    public async Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListAsync(string? productType, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<MockupTemplateResponseDto>>(MockupErrors.Unauthenticated("view"));

        var normalizedType = productType?.Trim().ToLowerInvariant();

        var items = await templates.Query()
            .Where(template => template.IsActive == true
                && (template.UserId == null || template.UserId == userId)
                && (string.IsNullOrEmpty(normalizedType) || template.ProductType.ToLower() == normalizedType))
            .OrderByDescending(template => template.UsageCount)
            .ThenBy(template => template.Name)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MockupTemplateResponseDto>>(items.Select(template => Map(template, userId)).ToList());
    }

    public async Task<Result<BatchMockupSelectionResponseDto>> GetSelectionAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.Unauthenticated("view"));

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.BatchNotFound());

        return Result.Success(new BatchMockupSelectionResponseDto(
            batch.Id, ReadSelection(batch.Config), ReadColors(batch.Config), ReadTemplateColors(batch.Config)));
    }

    public async Task<Result<BatchMockupSelectionResponseDto>> ApplyAsync(Guid batchJobId, ApplyMockupTemplatesRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.Unauthenticated("update"));

        var validation = await applyValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<BatchMockupSelectionResponseDto>(validation.ToValidationError());
        }

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.BatchNotFound());
        // Selection is plain config read when mock-ups are generated, so it can change before or after
        // image generation — only not while a run is in flight.
        if (BatchJobStatuses.Active.Contains(batch.Status.ToLowerInvariant()))
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.JobGenerating());

        var candidates = await templates.Query()
            .Where(template => request.TemplateIds.Contains(template.Id) && template.IsActive == true)
            .Select(template => new { template.Id, template.Name, template.ProductType })
            .ToListAsync(cancellationToken);

        if (candidates.Count != request.TemplateIds.Count)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.TemplateNotFound());

        var batchTypes = await BatchProductTypesAsync(batchJobId, cancellationToken);
        var incompatible = batchTypes.Count > 0
            ? candidates.FirstOrDefault(template => !batchTypes.Contains(template.ProductType.ToLowerInvariant()))
            : null;
        if (incompatible is not null)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.IncompatibleTemplate(incompatible.Name));

        var colors = (request.GarmentColors ?? []).Select(color => color.ToUpperInvariant()).ToList();
        var colorsByTemplate = NormalizeTemplateColors(request.TemplateColors);
        batch.Config = WriteSelection(batch.Config, request.TemplateIds, colors, colorsByTemplate);
        batch.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batches.UpdateAsync(batch, saveChange: true, cancellationToken);

        return Result.Success(new BatchMockupSelectionResponseDto(batch.Id, request.TemplateIds, colors, colorsByTemplate));
    }

    public async Task<Result<MockupTemplateResponseDto>> CreateAsync(CreateMockupTemplateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("create"));

        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<MockupTemplateResponseDto>(validation.ToValidationError());
        }

        if (await IsNameTakenAsync(request.Name, null, cancellationToken))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        }

        if (!mapService.IsAvailable)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ProcessingUnavailable());

        var templateId = Guid.NewGuid();
        var storageKey = StorageKey(templateId);
        var photo = await ReadAllAsync(request.BaseImage!.Content, cancellationToken);
        var prepared = await TryPrepareAsync(photo, cancellationToken);
        var position = new MockupPosition(request.X, request.Y, request.Width, request.Height);
        // Checked against the analyzed photo first, so a rejected request leaves nothing stored.
        if (prepared is { WidthPx: > 0, HeightPx: > 0 } && !FitsWithin(position, prepared.WidthPx, prepared.HeightPx))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());

        var uploaded = await UploadPhotoAsync(request.BaseImage, photo, storageKey, cancellationToken);

        if (!FitsWithin(position, uploaded.WidthPx, uploaded.HeightPx))
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var template = new MockupTemplate
        {
            Id = templateId,
            UserId = userId,
            Name = request.Name.Trim(),
            ProductType = request.ProductType,
            BaseImageUrl = uploaded.Url,
            PrintAreaConfig = MockupRules.SerializePrintArea(position),
            OutputWidthPx = uploaded.WidthPx,
            OutputHeightPx = uploaded.HeightPx,
            IsSystemTemplate = false,
            IsActive = true,
            UsageCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        var recolorError = await ApplyPreparedAsync(template, prepared, request.AllowRecolor, cancellationToken)
            ?? ApplyGarmentColor(template, request.GarmentColor);
        if (recolorError is not null)
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            return Result.Failure<MockupTemplateResponseDto>(recolorError);
        }

        try
        {
            await templates.AddAsync(template, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);

            if (await IsNameTakenAsync(request.Name, null, cancellationToken))
            {
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
            }

            throw;
        }
        catch
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            throw;
        }

        return Result.Success(Map(template, userId));
    }

    public async Task<Result<MockupTemplateResponseDto>> UpdateAsync(Guid id, UpdateMockupTemplateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("update"));

        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<MockupTemplateResponseDto>(validation.ToValidationError());
        }

        var template = await templates.GetByIdAsync(id, cancellationToken);
        if (template is null || template.IsActive != true)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.TemplateNotFoundSingle());
        if (template.UserId is null || template.UserId != userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.UpdateNotOwner());

        if (await IsNameTakenAsync(request.Name, id, cancellationToken))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        }

        var outputWidth = template.OutputWidthPx;
        var outputHeight = template.OutputHeightPx;
        var storageKey = StorageKey(template.Id);

        // The helper images are derived once per photo: again only for a new photo, or when they are missing.
        var reprocess = request.BaseImage is not null || !MockupRules.HasCurrentMaps(template);
        if (reprocess && !mapService.IsAvailable)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ProcessingUnavailable());

        byte[]? photo = null;
        if (request.BaseImage is not null)
            photo = await ReadAllAsync(request.BaseImage.Content, cancellationToken);
        else if (reprocess)
            photo = await TryDownloadAsync(template.BaseImageUrl, cancellationToken);

        var position = new MockupPosition(request.X, request.Y, request.Width, request.Height);
        PreparedBasePhoto? prepared = null;
        if (photo is not null)
        {
            prepared = await TryPrepareAsync(photo, cancellationToken);
            // Checked before anything is stored, so a rejected request does not replace the photo.
            if (prepared is { WidthPx: > 0, HeightPx: > 0 } && !FitsWithin(position, prepared.WidthPx, prepared.HeightPx))
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());

            if (request.BaseImage is not null)
            {
                var uploaded = await UploadPhotoAsync(request.BaseImage, photo, storageKey, cancellationToken);
                template.BaseImageUrl = uploaded.Url;
                outputWidth = uploaded.WidthPx;
                outputHeight = uploaded.HeightPx;
            }
        }

        if (!FitsWithin(position, outputWidth, outputHeight))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());
        }

        if (photo is not null)
        {
            if (await ApplyPreparedAsync(template, prepared, request.AllowRecolor, cancellationToken) is { } recolorError)
                return Result.Failure<MockupTemplateResponseDto>(recolorError);
        }
        else
        {
            if (request.AllowRecolor && !template.GarmentIsLight)
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.NotRecolorable("the garment is too dark or could not be separated from the background."));
            template.AllowRecolor = request.AllowRecolor;
        }

        if (ApplyGarmentColor(template, request.GarmentColor) is { } colorError)
            return Result.Failure<MockupTemplateResponseDto>(colorError);

        template.Name = request.Name.Trim();
        template.ProductType = request.ProductType;
        template.PrintAreaConfig = MockupRules.SerializePrintArea(position);
        template.OutputWidthPx = outputWidth;
        template.OutputHeightPx = outputHeight;
        template.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await templates.UpdateAsync(template, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsNameTakenAsync(request.Name, id, cancellationToken))
            {
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
            }

            throw;
        }

        return Result.Success(Map(template, userId));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure(MockupErrors.Unauthenticated("delete"));

        var template = await templates.GetByIdAsync(id, cancellationToken);
        if (template is null || template.IsActive != true)
            return Result.Failure(MockupErrors.TemplateNotFoundSingle());
        if (template.UserId is null || template.UserId != userId)
            return Result.Failure(MockupErrors.DeleteNotOwner());

        // No deleted_at column on mockup_templates, and product_mockup_templates/mockup_images both
        // FK-reference this row — deactivating (matching the IsActive filter ListAsync already uses)
        // is the safe option instead of a hard delete.
        template.IsActive = false;
        template.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await templates.UpdateAsync(template, saveChange: true, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<MockupImageResponseDto>> GenerateCompositeAsync(Guid designImageId, GenerateMockupImageRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.Unauthenticated("generate"));

        var validation = await generateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<MockupImageResponseDto>(validation.ToValidationError());
        }

        var designImage = await designImages.Query()
            .SingleOrDefaultAsync(image => image.Id == designImageId && image.DeletedAt == null, cancellationToken);
        if (designImage is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.DesignImageNotFound());

        var product = await products.Query()
            .SingleOrDefaultAsync(item => item.Id == designImage.ProductId, cancellationToken);
        if (product is null || product.UserId != userId)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.DesignImageNotFound());

        var template = await templates.Query()
            .SingleOrDefaultAsync(item => item.Id == request.MockupTemplateId && item.IsActive == true
                && (item.UserId == null || item.UserId == userId), cancellationToken);
        if (template is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.TemplateNotFoundSingle());
        if (!string.Equals(template.ProductType, product.ProductType, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<MockupImageResponseDto>(MockupErrors.IncompatibleTemplate(template.Name));

        var position = request.X is not null
            ? new MockupPosition(request.X.Value, request.Y!.Value, request.Width!.Value, request.Height!.Value)
            : MockupRules.ParsePrintArea(template.PrintAreaConfig);
        if (position is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.InvalidPrintArea());

        var layers = MockupRules.Layers(template, designImage, request.GarmentColor);
        if (request.GarmentColor is not null && layers.GarmentColor is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.RecolorNotAllowed(template.Name));

        string compositeUrl;
        try
        {
            compositeUrl = compositor.BuildCompositeUrl(template.BaseImageUrl, designImage.StorageKey, position, layers);
        }
        catch (InvalidOperationException)
        {
            // Seeded/system templates can carry a placeholder BaseImageUrl (no real Cloudinary
            // "/upload/" segment) — surface that as a normal error instead of an unhandled 500.
            return Result.Failure<MockupImageResponseDto>(MockupErrors.TemplateNotUsable(template.Name));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mockupImage = new MockupImage
        {
            Id = Guid.NewGuid(),
            DesignImageId = designImage.Id,
            ProductId = product.Id,
            MockupTemplateId = template.Id,
            StorageProvider = "cloudinary",
            // Informational only: the composite is a Cloudinary-rendered transformation URL, not a
            // separately uploaded asset, so there is no real public id to store here.
            StorageKey = $"mockups/{template.Id:N}/{designImage.Id:N}",
            MockupImageUrl = compositeUrl,
            MockupWidthPx = template.OutputWidthPx,
            MockupHeightPx = template.OutputHeightPx,
            GenerationTimeSeconds = 0,
            ApprovalStatus = "pending",
            BatchJobProductId = designImage.BatchJobProductId,
            GarmentColor = layers.GarmentColor,
            SourceType = MockupSources.Generated,
            MetadataRevision = MockupSources.FirstRevision,
            Role = MockupSources.DefaultRole,
            Regions = MockupSources.NoRegions,
            ArtworkGroupKey = MockupSources.ArtworkGroupKey(designImage.Id),
            VariantKey = layers.GarmentColor,
            CreatedAt = now
        };

        await mockupImages.AddAsync(mockupImage, cancellationToken: cancellationToken);
        template.UsageCount = (template.UsageCount ?? 0) + 1;
        template.UpdatedAt = now;
        await templates.UpdateAsync(template, saveChange: true, cancellationToken);

        return Result.Success(MapImage(mockupImage));
    }

    public async Task<Result<GenerateAllMockupsResultDto>> GenerateAllAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.Unauthenticated("generate"));

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.BatchNotFound());
        if (string.Equals(batch.Status, BatchJobStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.JobNotReady());

        return await GenerateAllCoreAsync(batch, userId, cancellationToken);
    }

    public async Task<Result<GenerateAllMockupsResultDto>> GenerateAllForJobAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        var batch = await batches.Query()
            .SingleOrDefaultAsync(job => job.Id == batchJobId && job.DeletedAt == null, cancellationToken);
        if (batch is null)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.BatchNotFound());
        if (batch.Status is not (BatchJobStatuses.Completed or BatchJobStatuses.PartiallyCompleted))
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.JobNotReady());
        // The Seller reviews the designs first and then asks for the mock-ups themselves.
        if (ReadRequireApproval(batch.Config) == true)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.ApprovalPending());

        return await GenerateAllCoreAsync(batch, batch.UserId, cancellationToken);
    }

    private async Task<Result<GenerateAllMockupsResultDto>> GenerateAllCoreAsync(BatchJob batch, Guid userId, CancellationToken cancellationToken)
    {
        var batchJobId = batch.Id;
        var selectedIds = ReadSelection(batch.Config);
        if (selectedIds.Count == 0)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.NoTemplatesSelected());
        var selectedColors = ReadColors(batch.Config);
        var colorsByTemplate = ReadTemplateColors(batch.Config);

        var selectedTemplates = await templates.Query()
            .Where(template => selectedIds.Contains(template.Id) && template.IsActive == true
                && (template.UserId == null || template.UserId == userId))
            .ToListAsync(cancellationToken);

        var rowIds = await rows.Query()
            .Where(row => row.BatchJobId == batchJobId)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var candidateImages = await designImages.Query()
            .Where(image => image.BatchJobProductId != null && rowIds.Contains(image.BatchJobProductId!.Value) && image.DeletedAt == null)
            .ToListAsync(cancellationToken);

        List<DesignImage> targetImages;
        int noDesignImageCount;
        var noApprovedImageCount = 0;
        if (ReadRequireApproval(batch.Config) is null)
        {
            // A job started before approval existed: one image per product row, the lowest variation
            // index (then earliest), standing in for "the approved one".
            targetImages = candidateImages
                .GroupBy(image => image.BatchJobProductId!.Value)
                .Select(group => group.OrderBy(image => image.VariationIndex).ThenBy(image => image.CreatedAt).First())
                .ToList();
            noDesignImageCount = rowIds.Count - targetImages.Count;
        }
        else
        {
            // Every approved image gets its mock-ups, so a product with several approved variations has several.
            targetImages = candidateImages
                .Where(image => image.ApprovalStatus == ApprovalStatuses.Approved)
                .OrderBy(image => image.VariationIndex)
                .ThenBy(image => image.CreatedAt)
                .ToList();
            var rowsWithImages = candidateImages.Select(image => image.BatchJobProductId!.Value).Distinct().Count();
            noDesignImageCount = rowIds.Count - rowsWithImages;
            noApprovedImageCount = rowsWithImages - targetImages.Select(image => image.BatchJobProductId!.Value).Distinct().Count();
        }

        var productIds = targetImages.Select(image => image.ProductId).Distinct().ToList();
        var productsById = (await products.Query().Where(product => productIds.Contains(product.Id)).ToListAsync(cancellationToken))
            .ToDictionary(product => product.Id);

        var designImageIds = targetImages.Select(image => image.Id).ToList();
        var selectedTemplateIds = selectedTemplates.Select(template => template.Id).ToHashSet();
        var existingImages = await mockupImages.Query()
            .Where(image => image.DesignImageId != null && designImageIds.Contains(image.DesignImageId.Value)
                && image.MockupTemplateId != null && selectedTemplateIds.Contains(image.MockupTemplateId.Value))
            .ToListAsync(cancellationToken);
        // Keyed by the composite URL too: the URL encodes the template's base photo and print area,
        // so a row made before the template was edited no longer matches and gets regenerated.
        var existingByKey = existingImages
            .GroupBy(image => (image.DesignImageId, image.MockupTemplateId, image.MockupImageUrl))
            .ToDictionary(group => group.Key, group => group.First());

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = new List<MockupImage>();
        var current = new List<MockupImage>();
        var errors = new List<string>();
        var usageIncrements = new Dictionary<Guid, int>();
        var noCompatibleTemplateCount = 0;

        void AddError(string message)
        {
            if (errors.Count < 50 && !errors.Contains(message)) errors.Add(message);
        }

        foreach (var designImage in targetImages)
        {
            if (!productsById.TryGetValue(designImage.ProductId, out var product))
                continue;

            var matchingTemplates = selectedTemplates
                .Where(template => string.Equals(template.ProductType, product.ProductType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingTemplates.Count == 0)
            {
                noCompatibleTemplateCount++;
                continue;
            }

            foreach (var template in matchingTemplates)
            {
                var position = MockupRules.ParsePrintArea(template.PrintAreaConfig);
                if (position is null)
                {
                    AddError($"{template.Name}: invalid print area.");
                    continue;
                }

                // A recolorable template gets one mock-up per color picked for the batch, else its own
                // color; any other template keeps the photo's color.
                var recolorable = template.AllowRecolor && MockupRules.HasCurrentMaps(template);
                // The colors chosen for this template; templates without any of their own share the batch's list.
                var palette = colorsByTemplate.TryGetValue(template.Id, out var ownColors) ? ownColors : selectedColors;
                var colors = recolorable && palette.Count > 0
                    ? palette.Cast<string?>().ToList()
                    : [recolorable ? template.GarmentColor : null];

                foreach (var color in colors)
                {
                    var layers = MockupRules.Layers(template, designImage, color);
                    string compositeUrl;
                    try
                    {
                        compositeUrl = compositor.BuildCompositeUrl(template.BaseImageUrl, designImage.StorageKey, position, layers);
                    }
                    catch (InvalidOperationException)
                    {
                        AddError($"{template.Name}: does not have a usable base photo yet.");
                        break;
                    }

                    if (existingByKey.TryGetValue((designImage.Id, template.Id, compositeUrl), out var upToDate))
                    {
                        current.Add(upToDate);
                        continue;
                    }

                    var mockupImage = new MockupImage
                    {
                        Id = Guid.NewGuid(),
                        DesignImageId = designImage.Id,
                        ProductId = product.Id,
                        MockupTemplateId = template.Id,
                        StorageProvider = "cloudinary",
                        StorageKey = $"mockups/{template.Id:N}/{designImage.Id:N}",
                        MockupImageUrl = compositeUrl,
                        MockupWidthPx = template.OutputWidthPx,
                        MockupHeightPx = template.OutputHeightPx,
                        GenerationTimeSeconds = 0,
                        ApprovalStatus = "pending",
                        BatchJobProductId = designImage.BatchJobProductId,
                        GarmentColor = layers.GarmentColor,
                        SourceType = MockupSources.Generated,
                        MetadataRevision = MockupSources.FirstRevision,
                        Role = MockupSources.DefaultRole,
                        Regions = MockupSources.NoRegions,
                        ArtworkGroupKey = MockupSources.ArtworkGroupKey(designImage.Id),
                        VariantKey = layers.GarmentColor,
                        CreatedAt = now
                    };
                    created.Add(mockupImage);
                    current.Add(mockupImage);
                    usageIncrements[template.Id] = usageIncrements.GetValueOrDefault(template.Id) + 1;
                }
            }
        }

        foreach (var mockupImage in created)
            await mockupImages.AddAsync(mockupImage, cancellationToken: cancellationToken);

        foreach (var template in selectedTemplates)
        {
            if (!usageIncrements.TryGetValue(template.Id, out var increment))
                continue;
            template.UsageCount = (template.UsageCount ?? 0) + increment;
            template.UpdatedAt = now;
            await templates.UpdateAsync(template, cancellationToken: cancellationToken);
        }

        if (created.Count > 0 || usageIncrements.Count > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        var allImages = current.Select(MapImage).ToList();

        return Result.Success(new GenerateAllMockupsResultDto(
            created.Count, noDesignImageCount, noCompatibleTemplateCount, errors, allImages, noApprovedImageCount));
    }

    private static MockupImageResponseDto MapImage(MockupImage mockupImage) =>
        // A composited mock-up always has its design, template and URL; only an uploaded one may lack them.
        new(mockupImage.Id, mockupImage.ProductId, mockupImage.DesignImageId!.Value, mockupImage.MockupTemplateId!.Value,
            mockupImage.MockupImageUrl!, mockupImage.MockupWidthPx, mockupImage.MockupHeightPx, mockupImage.ApprovalStatus,
            mockupImage.GarmentColor);

    private async Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var candidate = name.Trim().ToLowerInvariant();
        return await templates.Query()
            .AnyAsync(template => template.Name.ToLower() == candidate && template.Id != excludeId, cancellationToken);
    }

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    // Preparing only adds realism; a photo that cannot be analyzed is still a usable base photo.
    private async Task<PreparedBasePhoto?> TryPrepareAsync(byte[] photo, CancellationToken cancellationToken, bool preview = false)
    {
        try
        {
            return await mapService.PrepareAsync(photo, preview, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not analyze a mock-up base photo.");
            return null;
        }
    }

    private async Task<byte[]?> TryDownloadAsync(string baseImageUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await mapService.DownloadAsync(baseImageUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not download mock-up base photo {Url}.", baseImageUrl);
            return null;
        }
    }

    // The upload's stream was already read for analysis, so the photo is sent from its bytes.
    private async Task<PublicImageUploadResult> UploadPhotoAsync(UploadFileDto upload, byte[] photo, string storageKey, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(photo);
        return await images.UploadImageWithMetadataAsync(new UploadFileDto(upload.FileName, upload.ContentType, stream.Length, stream), storageKey, cancellationToken);
    }

    /// <summary>
    /// Stores the helper images prepared from the template's current photo and records what they
    /// allow. A failure only turns the realism off; the returned error is set when recoloring was
    /// requested but the photo is unsuitable.
    /// </summary>
    private async Task<Error?> ApplyPreparedAsync(MockupTemplate template, PreparedBasePhoto? prepared, bool allowRecolor, CancellationToken cancellationToken)
    {
        template.PrintMapsSourceUrl = null;
        template.PrintMapsVersion = null;
        template.AllowRecolor = false;
        template.GarmentIsLight = false;
        if (prepared is null)
            return allowRecolor ? MockupErrors.NotRecolorable("the photo could not be analyzed.") : null;

        var version = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        try
        {
            await mapService.StoreHelpersAsync(MockupRules.HelperKeyPrefix(template.Id, version), prepared, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not store print maps for mock-up template {TemplateId}.", template.Id);
            return allowRecolor ? MockupErrors.NotRecolorable("the photo could not be analyzed.") : null;
        }

        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = version;

        var problem = MockupRules.RecolorProblem(prepared.Stats);
        template.GarmentIsLight = problem is null;
        if (!allowRecolor)
            return null;
        if (problem is not null)
            return MockupErrors.NotRecolorable(problem);

        template.AllowRecolor = true;
        return null;
    }

    public async Task<Result<GarmentMaskPreviewResponseDto>> PreviewGarmentMaskAsync(UploadFileDto? photo, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.Unauthenticated("preview"));
        if (!MockupImageValidators.IsSupportedImage(photo) || !MockupImageValidators.IsWithinSizeLimit(photo))
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.InvalidPreviewPhoto());
        if (!mapService.IsAvailable)
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.ProcessingUnavailable());

        var bytes = await ReadAllAsync(photo!.Content, cancellationToken);
        var prepared = await TryPrepareAsync(bytes, cancellationToken, preview: true);
        if (prepared is null)
            return Result.Success(new GarmentMaskPreviewResponseDto(false, "the photo could not be analyzed.", null));

        var problem = MockupRules.RecolorProblem(prepared.Stats);
        return Result.Success(new GarmentMaskPreviewResponseDto(
            problem is null, problem,
            $"data:image/png;base64,{Convert.ToBase64String(prepared.MaskPng)}"));
    }

    private static Error? ApplyGarmentColor(MockupTemplate template, string? garmentColor)
    {
        if (garmentColor is not null && !template.AllowRecolor)
            return MockupErrors.RecolorNotAllowed(template.Name);

        template.GarmentColor = template.AllowRecolor ? garmentColor?.ToUpperInvariant() : null;
        return null;
    }

    private async Task TryDeleteImageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await images.DeleteImageAsync(storageKey, cancellationToken);
        }
        catch (Exception)
        {
            // Best effort: the database write already failed, so the original exception must reach the caller unchanged.
        }
    }

    private static bool FitsWithin(MockupPosition position, int outputWidth, int outputHeight) =>
        position.X >= 0 && position.Y >= 0 && position.Width > 0 && position.Height > 0
        && position.X + position.Width <= outputWidth && position.Y + position.Height <= outputHeight;

    private static string StorageKey(Guid templateId) => MockupRules.BaseImageKey(templateId);

    private MockupTemplateResponseDto Map(MockupTemplate template, Guid userId)
    {
        var mapsReady = MockupRules.HasCurrentMaps(template);
        var recolor = mapsReady && template.AllowRecolor;
        return new(template.Id, template.Name, template.ProductType, template.BaseImageUrl, template.PreviewImageUrl,
            template.PrintAreaConfig, template.OutputWidthPx, template.OutputHeightPx, template.UsageCount ?? 0,
            template.IsSystemTemplate == true, template.UserId == userId, mapsReady, recolor,
            mapsReady ? compositor.BuildAssetUrl(template.BaseImageUrl, MockupRules.GarmentMaskKey(template)) : null,
            recolor ? template.GarmentColor : null);
    }

    private async Task<BatchJob?> FindOwnedBatchAsync(Guid batchJobId, CancellationToken cancellationToken)
    {
        var userId = currentUser.TryGetUserId();
        if (userId is null)
            return null;

        return await batches.Query()
            .Where(batch => batch.Id == batchJobId && batch.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<HashSet<string>> BatchProductTypesAsync(Guid batchJobId, CancellationToken cancellationToken)
    {
        var productIds = await rows.Query()
            .Where(row => row.BatchJobId == batchJobId && row.ProductId != null)
            .Select(row => row.ProductId!.Value)
            .ToListAsync(cancellationToken);

        if (productIds.Count == 0)
            return [];

        return (await products.Query()
                .Where(product => productIds.Contains(product.Id))
                .Select(product => product.ProductType.ToLower())
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<Guid> ReadSelection(string config)
    {
        try
        {
            var node = JsonNode.Parse(config);
            var ids = node?[MockupRules.ConfigKey]?.AsArray();
            if (ids is null)
                return [];

            return ids
                .Select(id => Guid.TryParse(id?.ToString(), out var value) ? value : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadColors(string config)
    {
        try
        {
            return JsonNode.Parse(config)?[MockupRules.GarmentColorsConfigKey]?.AsArray()
                .Select(color => color?.ToString())
                .Where(color => color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, MockupRules.GarmentColorPattern))
                .Select(color => color!.ToUpperInvariant())
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Kept next to the selection in the job's config; written by ApplyAsync, read when mock-ups are made.
    private const string TemplateColorsConfigKey = "mockupTemplateColors";

    /// <summary>The job's approval mode, or null for a job started before approval existed.</summary>
    private static bool? ReadRequireApproval(string config)
    {
        try
        {
            return (bool?)JsonNode.Parse(config)?[GenerationConfigKeys.RequireApproval];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<string>> ReadTemplateColors(string config)
    {
        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        try
        {
            if (JsonNode.Parse(config)?[TemplateColorsConfigKey] is not JsonObject byTemplate)
                return result;

            foreach (var (key, value) in byTemplate)
            {
                if (!Guid.TryParse(key, out var templateId) || value is not JsonArray array)
                    continue;

                var colors = array
                    .Select(color => color?.ToString())
                    .Where(color => color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, MockupRules.GarmentColorPattern))
                    .Select(color => color!.ToUpperInvariant())
                    .ToList();
                if (colors.Count > 0)
                    result[templateId] = colors;
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }

        return result;
    }

    // Uppercased, and without templates that were given no color (they use the shared list).
    private static IReadOnlyDictionary<Guid, IReadOnlyList<string>> NormalizeTemplateColors(IReadOnlyDictionary<Guid, IReadOnlyList<string>>? requested) =>
        (requested ?? new Dictionary<Guid, IReadOnlyList<string>>())
            .Where(entry => entry.Value is { Count: > 0 })
            .ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value.Select(color => color.ToUpperInvariant()).ToList());

    private static string WriteSelection(
        string config,
        IReadOnlyList<Guid> templateIds,
        IReadOnlyList<string> garmentColors,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> colorsByTemplate)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(config)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        var ids = new JsonArray();
        foreach (var id in templateIds)
        {
            ids.Add(id.ToString());
        }

        root[MockupRules.ConfigKey] = ids;
        root[MockupRules.GarmentColorsConfigKey] = new JsonArray(garmentColors.Select(color => (JsonNode?)JsonValue.Create(color)).ToArray());

        if (colorsByTemplate.Count == 0)
        {
            root.Remove(TemplateColorsConfigKey);
        }
        else
        {
            var byTemplate = new JsonObject();
            foreach (var (templateId, colors) in colorsByTemplate)
                byTemplate[templateId.ToString()] = new JsonArray(colors.Select(color => (JsonNode?)JsonValue.Create(color)).ToArray());
            root[TemplateColorsConfigKey] = byTemplate;
        }

        return root.ToJsonString();
    }
}
