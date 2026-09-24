using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.BatchProductPrompts.Dtos.Request;
using APCS.Application.Features.BatchProductPrompts.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.BatchProductPrompts;

/// <summary>Reads and stores per-row design prompt overrides.</summary>
public sealed class BatchProductPromptService(
    ICurrentUser currentUser,
    IRepository<BatchJobProduct> rows,
    IRepository<BatchJob> batches,
    IRepository<Product> products,
    IRepository<DesignTemplate> templates,
    IRepository<StyleArtPreset> styles,
    IValidator<UpdateBatchProductPromptRequestDto> saveValidator,
    TimeProvider timeProvider) : IBatchProductPromptService
{
    public async Task<Result<BatchProductPromptResponseDto>> GetDefaultAsync(Guid rowId, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadOwnedAsync(rowId, cancellationToken);
        if (loaded is null)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.RowNotFound());

        var defaults = await DefaultComponentsAsync(loaded, cancellationToken);
        return Result.Success(Respond(loaded, defaults, defaults));
    }

    public async Task<Result<BatchProductPromptResponseDto>> GetEffectiveAsync(Guid rowId, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadOwnedAsync(rowId, cancellationToken);
        if (loaded is null)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.RowNotFound());

        var defaults = await DefaultComponentsAsync(loaded, cancellationToken);
        return Result.Success(Respond(loaded, defaults, EffectiveComponents(loaded.Row, defaults)));
    }

    public async Task<Result<BatchProductPromptResponseDto>> SaveAsync(Guid rowId, UpdateBatchProductPromptRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.Unauthenticated("update"));

        var validation = await saveValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<BatchProductPromptResponseDto>(validation.ToValidationError());
        }

        var loaded = await LoadOwnedAsync(rowId, cancellationToken);
        if (loaded is null)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.RowNotFound());
        if (!loaded.CanEdit)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.NotPending());

        var defaults = await DefaultComponentsAsync(loaded, cancellationToken);
        var components = new PromptComponents(
            request.Subject.Trim(),
            request.ArtStyle.Trim(),
            request.MoodTone.Trim(),
            PromptComposer.Normalize(request.NegativeTerms),
            PromptComposer.Normalize(request.Instructions));

        var effective = Compose(defaults, components);
        if (effective.Length > PromptRules.MaximumEffectiveLength)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.TooLong(effective.Length));

        loaded.Row.CustomSubject = components.Subject;
        loaded.Row.CustomArtStyle = components.ArtStyle;
        loaded.Row.CustomMoodTone = components.MoodTone;
        loaded.Row.CustomNegativeTerms = PromptComposer.NullIfEmpty(components.NegativeTerms);
        loaded.Row.CustomInstructions = PromptComposer.NullIfEmpty(components.Instructions);
        loaded.Row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await rows.UpdateAsync(loaded.Row, saveChange: true, cancellationToken);

        return Result.Success(Respond(loaded, defaults, components));
    }

    public async Task<Result<BatchProductPromptResponseDto>> RestoreAsync(Guid rowId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.Unauthenticated("restore"));

        var loaded = await LoadOwnedAsync(rowId, cancellationToken);
        if (loaded is null)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.RowNotFound());
        if (!loaded.CanEdit)
            return Result.Failure<BatchProductPromptResponseDto>(BatchProductPromptErrors.NotPending());

        loaded.Row.CustomSubject = null;
        loaded.Row.CustomArtStyle = null;
        loaded.Row.CustomMoodTone = null;
        loaded.Row.CustomNegativeTerms = null;
        loaded.Row.CustomInstructions = null;
        loaded.Row.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await rows.UpdateAsync(loaded.Row, saveChange: true, cancellationToken);

        var defaults = await DefaultComponentsAsync(loaded, cancellationToken);
        return Result.Success(Respond(loaded, defaults, defaults));
    }

    private async Task<LoadedRow?> LoadOwnedAsync(Guid rowId, CancellationToken cancellationToken)
    {
        var userId = currentUser.TryGetUserId();
        if (userId is null)
            return null;

        var row = await rows.Query()
            .Where(item => item.Id == rowId)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var batch = await batches.Query()
            .Where(item => item.Id == row.BatchJobId && item.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken);
        if (batch is null)
            return null;

        Product? product = null;
        if (row.ProductId.HasValue)
        {
            product = await products.Query()
                .Where(item => item.Id == row.ProductId.Value)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var canEdit = string.Equals(row.Status, "pending", StringComparison.OrdinalIgnoreCase)
            && string.Equals(batch.Status, "draft", StringComparison.OrdinalIgnoreCase);
        return new LoadedRow(row, batch, product, canEdit);
    }

    private Task<PromptComponents> DefaultComponentsAsync(LoadedRow loaded, CancellationToken cancellationToken) =>
        PromptDefaultsResolver.ResolveAsync(loaded.Product, templates, styles, cancellationToken);

    private static PromptComponents EffectiveComponents(BatchJobProduct row, PromptComponents defaults) =>
        defaults with
        {
            Subject = row.CustomSubject ?? defaults.Subject,
            ArtStyle = row.CustomArtStyle ?? defaults.ArtStyle,
            MoodTone = row.CustomMoodTone ?? defaults.MoodTone,
            NegativeTerms = row.CustomNegativeTerms ?? string.Empty,
            Instructions = row.CustomInstructions ?? string.Empty
        };

    private static string Compose(PromptComponents defaults, PromptComponents components) =>
        PromptComposer.Compose(
            defaults.BasePrompt,
            components.Subject,
            components.ArtStyle,
            components.MoodTone,
            components.NegativeTerms,
            components.Instructions,
            defaults.Niche,
            defaults.StyleModifiers);

    private static BatchProductPromptResponseDto Respond(LoadedRow loaded, PromptComponents defaults, PromptComponents effective)
    {
        var defaultPrompt = Compose(defaults, defaults);
        var effectivePrompt = Compose(defaults, effective);
        var customized = loaded.Row.CustomSubject is not null
            || loaded.Row.CustomArtStyle is not null
            || loaded.Row.CustomMoodTone is not null
            || loaded.Row.CustomNegativeTerms is not null
            || loaded.Row.CustomInstructions is not null;

        return new BatchProductPromptResponseDto(
            loaded.Row.Id,
            effective.Subject,
            effective.ArtStyle,
            effective.MoodTone,
            effective.NegativeTerms,
            effective.Instructions,
            defaultPrompt,
            defaults.BasePrompt,
            defaults.Niche,
            defaults.StyleModifiers,
            effectivePrompt,
            effectivePrompt.Length,
            customized,
            loaded.CanEdit);
    }

    private sealed record LoadedRow(BatchJobProduct Row, BatchJob Batch, Product? Product, bool CanEdit);
}
