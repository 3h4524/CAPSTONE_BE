using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
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
    IValidator<ApplyMockupTemplatesRequestDto> applyValidator,
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
