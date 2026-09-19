using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.BatchMockups;

/// <summary>Lists mock-up templates and stores per-batch mock-up selection.</summary>
public sealed class MockupTemplateService(
    ICurrentUser currentUser,
    IRepository<MockupTemplate> templates,
    IRepository<BatchJob> batches,
    IRepository<BatchJobProduct> rows,
    IRepository<Product> products,
    IRepository<ProductMockupTemplate> links,
    IRepository<MockupImage> outputs,
    IPublicImageService images,
    IValidator<ApplyMockupTemplatesRequestDto> applyValidator,
    IValidator<CreateMockupTemplateRequestDto> createValidator,
    IValidator<UpdateMockupTemplateRequestDto> updateValidator,
    TimeProvider timeProvider) : IMockupTemplateService
{
    public async Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListAsync(string? productType, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<MockupTemplateResponseDto>>(MockupErrors.Unauthenticated("view"));

        var normalizedType = productType?.Trim().ToLowerInvariant();

        var items = await templates.Query()
            .Where(template => template.IsActive == true
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
        if (!string.Equals(batch.Status, "Draft", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.NotDraft());

        var candidates = await templates.Query()
            .Where(template => request.TemplateIds.Contains(template.Id))
            .Select(template => new { template.Id, template.Name, template.ProductType, template.IsActive })
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

        if (request.BaseImage is null)
        {
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation("A blank mock-up image is required."));
        }

        if (await IsNameTakenAsync(request.Name, null, cancellationToken))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        }

        var templateId = Guid.NewGuid();
        var baseUrl = await images.UploadImageAsync(request.BaseImage, BaseStorageKey(templateId), cancellationToken);
        var previewUrl = request.Preview is null
            ? null
            : await images.UploadImageAsync(request.Preview, PreviewStorageKey(templateId), cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var template = new MockupTemplate
        {
            Id = templateId,
            UserId = userId,
            Name = request.Name.Trim(),
            ProductType = request.ProductType,
            BaseImageUrl = baseUrl,
            PreviewImageUrl = previewUrl,
            PrintAreaConfig = request.PrintAreaConfig,
            OutputWidthPx = request.OutputWidthPx,
            OutputHeightPx = request.OutputHeightPx,
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
        catch
        {
            await TryDeleteImageAsync(BaseStorageKey(templateId), cancellationToken);
            await TryDeleteImageAsync(PreviewStorageKey(templateId), cancellationToken);
            throw;
        }

        return Result.Success(Map(template, templateId));
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
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.TemplateNotFound());
        if (template.UserId is null || template.UserId != userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.UpdateNotOwner());

        if (await IsNameTakenAsync(request.Name, id, cancellationToken))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        }

        template.Name = request.Name.Trim();
        template.ProductType = request.ProductType;
        template.PrintAreaConfig = request.PrintAreaConfig;
        template.OutputWidthPx = request.OutputWidthPx;
        template.OutputHeightPx = request.OutputHeightPx;

        if (request.BaseImage is not null)
        {
            template.BaseImageUrl = await images.UploadImageAsync(
                request.BaseImage,
                BaseStorageKey(template.Id),
                cancellationToken);
        }

        if (request.Preview is not null)
        {
            template.PreviewImageUrl = await images.UploadImageAsync(
                request.Preview,
                PreviewStorageKey(template.Id),
                cancellationToken);
        }
        else if (request.DeletePreview && template.PreviewImageUrl is not null)
        {
            await images.DeleteImageAsync(PreviewStorageKey(template.Id), cancellationToken);
            template.PreviewImageUrl = null;
        }

        template.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await templates.UpdateAsync(template, saveChange: true, cancellationToken);
        return Result.Success(Map(template, template.Id));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure(MockupErrors.Unauthenticated("delete"));

        var template = await templates.GetByIdAsync(id, cancellationToken);
        if (template is null || template.IsActive != true)
            return Result.Failure(MockupErrors.TemplateNotFound());
        if (template.UserId is null || template.UserId != userId)
            return Result.Failure(MockupErrors.DeleteNotOwner());

        var inUse = await links.Query().AnyAsync(link => link.MockupTemplateId == id, cancellationToken)
            || await outputs.Query().AnyAsync(output => output.MockupTemplateId == id, cancellationToken);
        if (inUse)
            return Result.Failure(MockupErrors.TemplateInUse());

        await images.DeleteImageAsync(BaseStorageKey(template.Id), cancellationToken);
        if (template.PreviewImageUrl is not null)
        {
            await images.DeleteImageAsync(PreviewStorageKey(template.Id), cancellationToken);
        }

        await templates.RemoveAsync(template, saveChange: true, cancellationToken);
        return Result.Success();
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

    private async Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var candidate = name.Trim().ToLowerInvariant();
        return await templates.Query()
            .AnyAsync(
                template => template.Name.ToLower() == candidate && template.Id != excludeId,
                cancellationToken);
    }

    private async Task TryDeleteImageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await images.DeleteImageAsync(storageKey, cancellationToken);
        }
        catch (Exception)
        {
            // Best effort: the database write already failed, so the original
            // exception must reach the caller unchanged.
        }
    }

    private static string BaseStorageKey(Guid templateId) => $"mockup-templates/{templateId:N}/base";

    private static string PreviewStorageKey(Guid templateId) => $"mockup-templates/{templateId:N}/preview";

    private static MockupTemplateResponseDto Map(MockupTemplate template, Guid id) =>
        new(
            id,
            template.Name,
            template.ProductType,
            template.BaseImageUrl,
            template.PreviewImageUrl,
            template.PrintAreaConfig,
            template.OutputWidthPx,
            template.OutputHeightPx,
            template.UsageCount ?? 0,
            template.IsSystemTemplate == true,
            template.UserId is not null);
}
