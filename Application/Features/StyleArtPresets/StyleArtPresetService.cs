using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.StyleArtPresets;

/// <summary>Manages system art styles and seller-created styles for the image-generation select step.</summary>
public sealed class StyleArtPresetService(
    ICurrentUser currentUser,
    IRepository<StyleArtPreset> presets,
    IPublicImageService images,
    IValidator<CreateStyleArtPresetRequestDto> createValidator,
    IValidator<UpdateStyleArtPresetRequestDto> updateValidator,
    IValidator<string> quickCreateValidator,
    TimeProvider timeProvider) : IStyleArtPresetService
{
    public async Task<Result<IReadOnlyList<StyleArtPresetResponseDto>>> ListMineAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<StyleArtPresetResponseDto>>(StyleArtPresetErrors.Unauthenticated("view"));

        var rows = await presets.Query()
            .Where(preset => preset.IsActive && (preset.UserId == null || preset.UserId == userId))
            .OrderByDescending(preset => preset.UsageCount)
            .ThenBy(preset => preset.Name)
            .Select(preset => new
            {
                preset.Id,
                preset.Name,
                preset.Description,
                preset.StyleModifiers,
                preset.PreviewImageUrl,
                preset.Recommendations,
                preset.IsSystemTemplate,
                preset.UsageCount,
                preset.UserId
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<StyleArtPresetResponseDto> items = rows
            .Select(row => Map(row.Id, row.Name, row.Description, row.StyleModifiers, row.PreviewImageUrl, row.Recommendations, row.IsSystemTemplate, row.UserId == userId, row.UsageCount))
            .ToList();

        return Result.Success(items);
    }

    public async Task<Result<StyleArtPresetResponseDto>> GetMineAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.Unauthenticated("view"));

        var row = await presets.Query()
            .Where(item => item.Id == id && item.IsActive && (item.UserId == null || item.UserId == userId))
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Description,
                item.StyleModifiers,
                item.PreviewImageUrl,
                item.Recommendations,
                item.IsSystemTemplate,
                item.UsageCount,
                item.UserId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.NotFound());

        return Result.Success(Map(row.Id, row.Name, row.Description, row.StyleModifiers, row.PreviewImageUrl, row.Recommendations, row.IsSystemTemplate, row.UserId == userId, row.UsageCount));
    }

    public async Task<Result<StyleArtPresetResponseDto>> CreateAsync(CreateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.Unauthenticated("create"));

        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<StyleArtPresetResponseDto>(validation.ToValidationError());
        }

        if (request.Preview is null)
        {
            return Result.Failure<StyleArtPresetResponseDto>(Error.Validation("A preview image is required."));
        }

        if (await IsNameTakenAsync(request.Name, null, cancellationToken))
        {
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.DuplicateName());
        }

        var presetId = Guid.NewGuid();
        var storageKey = StorageKey(presetId);
        var previewUrl = await images.UploadImageAsync(request.Preview, storageKey, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var preset = new StyleArtPreset
        {
            Id = presetId,
            UserId = userId,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            StyleModifiers = request.StyleModifiers.Trim(),
            PreviewImageUrl = previewUrl,
            Recommendations = SerializeRecommendations(request.RecommendationsJson),
            IsSystemTemplate = false,
            IsActive = true,
            UsageCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await presets.AddAsync(preset, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);

            if (await IsNameTakenAsync(request.Name, null, cancellationToken))
            {
                return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.DuplicateName());
            }

            throw;
        }
        catch
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            throw;
        }

        return Result.Success(Map(preset, isMine: true));
    }

    public async Task<Result<StyleArtPresetResponseDto>> QuickCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.Unauthenticated("create"));

        var validation = await quickCreateValidator.ValidateAsync(name, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<StyleArtPresetResponseDto>(validation.ToValidationError());
        }

        if (await IsNameTakenAsync(name, null, cancellationToken))
        {
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.DuplicateName());
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var preset = new StyleArtPreset
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name.Trim(),
            Description = string.Empty,
            StyleModifiers = string.Empty,
            PreviewImageUrl = null,
            Recommendations = "[]",
            IsSystemTemplate = false,
            IsActive = true,
            UsageCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        await presets.AddAsync(preset, saveChange: true, cancellationToken);
        return Result.Success(Map(preset, isMine: true));
    }

    public async Task<Result<StyleArtPresetResponseDto>> UpdateAsync(Guid id, UpdateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.Unauthenticated("update"));

        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<StyleArtPresetResponseDto>(validation.ToValidationError());
        }

        var preset = await presets.GetByIdAsync(id, cancellationToken);
        if (preset is null || !preset.IsActive)
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.NotFound());
        if (preset.UserId is null || preset.UserId != userId)
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.UpdateNotOwner());

        if (await IsNameTakenAsync(request.Name, id, cancellationToken))
        {
            return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.DuplicateName());
        }

        preset.Name = request.Name.Trim();
        preset.Description = request.Description.Trim();
        preset.StyleModifiers = request.StyleModifiers.Trim();
        preset.Recommendations = SerializeRecommendations(request.RecommendationsJson);

        if (request.Preview is not null)
        {
            preset.PreviewImageUrl = await images.UploadImageAsync(
                request.Preview,
                StorageKey(preset.Id),
                cancellationToken);
        }
        else if (request.DeletePreview && preset.PreviewImageUrl is not null)
        {
            await images.DeleteImageAsync(StorageKey(preset.Id), cancellationToken);
            preset.PreviewImageUrl = null;
        }

        preset.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await presets.UpdateAsync(preset, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsNameTakenAsync(request.Name, id, cancellationToken))
            {
                return Result.Failure<StyleArtPresetResponseDto>(StyleArtPresetErrors.DuplicateName());
            }

            throw;
        }

        return Result.Success(Map(preset, isMine: true));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure(StyleArtPresetErrors.Unauthenticated("delete"));

        var preset = await presets.GetByIdAsync(id, cancellationToken);
        if (preset is null || !preset.IsActive)
            return Result.Failure(StyleArtPresetErrors.NotFound());
        if (preset.UserId is null || preset.UserId != userId)
            return Result.Failure(StyleArtPresetErrors.DeleteNotOwner());

        await presets.RemoveAsync(preset, saveChange: true, cancellationToken);

        if (preset.PreviewImageUrl is not null)
        {
            await TryDeleteImageAsync(StorageKey(preset.Id), cancellationToken);
        }

        return Result.Success();
    }

    private async Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var candidate = name.Trim().ToLowerInvariant();
        return await presets.Query()
            .AnyAsync(
                preset => preset.Name.ToLower() == candidate && preset.Id != excludeId,
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

    private static string StorageKey(Guid presetId) => $"style-art-presets/{presetId:N}";

    private static StyleArtPresetResponseDto Map(StyleArtPreset preset, bool isMine) =>
        Map(preset.Id, preset.Name, preset.Description, preset.StyleModifiers, preset.PreviewImageUrl, preset.Recommendations, preset.IsSystemTemplate, isMine, preset.UsageCount);

    private static StyleArtPresetResponseDto Map(
        Guid id,
        string name,
        string description,
        string styleModifiers,
        string? previewImageUrl,
        string recommendations,
        bool isSystemTemplate,
        bool isMine,
        int usageCount) =>
        new(id, name, description, styleModifiers, previewImageUrl, ToRecommendations(recommendations), isSystemTemplate, isMine, usageCount);

    private static IReadOnlyList<string> ToRecommendations(string source) =>
        StyleArtPresetRules.TryParseRecommendations(source, out var recommendations) ? recommendations : [];

    private static string SerializeRecommendations(string? source)
    {
        if (!StyleArtPresetRules.TryParseRecommendations(source, out var recommendations))
        {
            return "[]";
        }

        return JsonSerializer.Serialize(recommendations);
    }
}
