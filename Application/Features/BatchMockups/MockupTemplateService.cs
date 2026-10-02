using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

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
    IUnitOfWork unitOfWork,
    IValidator<ApplyMockupTemplatesRequestDto> applyValidator,
    IValidator<CreateMockupTemplateRequestDto> createValidator,
    IValidator<UpdateMockupTemplateRequestDto> updateValidator,
    IValidator<GenerateMockupImageRequestDto> generateValidator,
    TimeProvider timeProvider) : IMockupTemplateService
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
            .Select(template => new MockupTemplateResponseDto(
                template.Id,
                template.Name,
                template.ProductType,
                template.BaseImageUrl,
                template.PreviewImageUrl,
                template.PrintAreaConfig,
                template.OutputWidthPx,
                template.OutputHeightPx,
                template.UsageCount ?? 0,
                template.IsSystemTemplate == true,
                template.UserId == userId))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MockupTemplateResponseDto>>(items);
    }

    public async Task<Result<BatchMockupSelectionResponseDto>> GetSelectionAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.Unauthenticated("view"));

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.BatchNotFound());

        return Result.Success(new BatchMockupSelectionResponseDto(batch.Id, ReadSelection(batch.Config)));
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

        batch.Config = WriteSelection(batch.Config, request.TemplateIds);
        batch.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batches.UpdateAsync(batch, saveChange: true, cancellationToken);

        return Result.Success(new BatchMockupSelectionResponseDto(batch.Id, request.TemplateIds));
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

        var templateId = Guid.NewGuid();
        var storageKey = StorageKey(templateId);
        var uploaded = await images.UploadImageWithMetadataAsync(request.BaseImage!, storageKey, cancellationToken);

        var position = new MockupPosition(request.X, request.Y, request.Width, request.Height);
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

        if (request.BaseImage is not null)
        {
            var uploaded = await images.UploadImageWithMetadataAsync(request.BaseImage, storageKey, cancellationToken);
            template.BaseImageUrl = uploaded.Url;
            outputWidth = uploaded.WidthPx;
            outputHeight = uploaded.HeightPx;
        }

        var position = new MockupPosition(request.X, request.Y, request.Width, request.Height);
        if (!FitsWithin(position, outputWidth, outputHeight))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());
        }

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

        string compositeUrl;
        try
        {
            compositeUrl = compositor.BuildCompositeUrl(template.BaseImageUrl, designImage.StorageKey, position);
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

        var selectedIds = ReadSelection(batch.Config);
        if (selectedIds.Count == 0)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.NoTemplatesSelected());

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

        // One target image per product row: lowest variation index (then earliest), as a stand-in
        // for "the approved one" until SRS 3.5.12 Approve/Reject Image exists.
        var targetImages = candidateImages
            .GroupBy(image => image.BatchJobProductId!.Value)
            .Select(group => group.OrderBy(image => image.VariationIndex).ThenBy(image => image.CreatedAt).First())
            .ToList();
        var noDesignImageCount = rowIds.Count - targetImages.Count;

        var productIds = targetImages.Select(image => image.ProductId).Distinct().ToList();
        var productsById = (await products.Query().Where(product => productIds.Contains(product.Id)).ToListAsync(cancellationToken))
            .ToDictionary(product => product.Id);

        var designImageIds = targetImages.Select(image => image.Id).ToList();
        var selectedTemplateIds = selectedTemplates.Select(template => template.Id).ToHashSet();
        var existingImages = await mockupImages.Query()
            .Where(image => designImageIds.Contains(image.DesignImageId) && selectedTemplateIds.Contains(image.MockupTemplateId))
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

                string compositeUrl;
                try
                {
                    compositeUrl = compositor.BuildCompositeUrl(template.BaseImageUrl, designImage.StorageKey, position);
                }
                catch (InvalidOperationException)
                {
                    AddError($"{template.Name}: does not have a usable base photo yet.");
                    continue;
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
                    CreatedAt = now
                };
                created.Add(mockupImage);
                current.Add(mockupImage);
                usageIncrements[template.Id] = usageIncrements.GetValueOrDefault(template.Id) + 1;
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
            created.Count, noDesignImageCount, noCompatibleTemplateCount, errors, allImages));
    }

    private static MockupImageResponseDto MapImage(MockupImage mockupImage) =>
        new(mockupImage.Id, mockupImage.ProductId, mockupImage.DesignImageId, mockupImage.MockupTemplateId,
            mockupImage.MockupImageUrl, mockupImage.MockupWidthPx, mockupImage.MockupHeightPx, mockupImage.ApprovalStatus);

    private async Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var candidate = name.Trim().ToLowerInvariant();
        return await templates.Query()
            .AnyAsync(template => template.Name.ToLower() == candidate && template.Id != excludeId, cancellationToken);
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

    private static string StorageKey(Guid templateId) => $"mockup-templates/{templateId:N}";

    private static MockupTemplateResponseDto Map(MockupTemplate template, Guid userId) =>
        new(template.Id, template.Name, template.ProductType, template.BaseImageUrl, template.PreviewImageUrl,
            template.PrintAreaConfig, template.OutputWidthPx, template.OutputHeightPx, template.UsageCount ?? 0,
            template.IsSystemTemplate == true, template.UserId == userId);

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

    private static string WriteSelection(string config, IReadOnlyList<Guid> templateIds)
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
        return root.ToJsonString();
    }
}
