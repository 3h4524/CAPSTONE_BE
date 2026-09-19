using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
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
    TimeProvider timeProvider) : IStyleArtPresetService
{
    public async Task<Result<IReadOnlyList<StyleArtPresetResponseDto>>> ListMineAsync(CancellationToken cancellationToken = default)
    {
        if (GetAuthenticatedUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<StyleArtPresetResponseDto>>(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in to view art styles."));

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
                preset.UserId
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<StyleArtPresetResponseDto> items = rows
            .Select(row => Map(row.Id, row.Name, row.Description, row.StyleModifiers, row.PreviewImageUrl, row.Recommendations, row.IsSystemTemplate, row.UserId == userId))
            .ToList();

        return Result.Success(items);
    }

    public async Task<Result<StyleArtPresetResponseDto>> GetMineAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (GetAuthenticatedUserId() is not Guid userId)
            return Result.Failure<StyleArtPresetResponseDto>(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in to view art styles."));

        var preset = await presets.Query()
            .Where(item => item.Id == id && item.IsActive && (item.UserId == null || item.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);

        if (preset is null)
            return Result.Failure<StyleArtPresetResponseDto>(Error.NotFound("StyleArtPresets.NotFound", "The art style was not found."));

        return Result.Success(Map(preset, preset.UserId == userId));
    }

    public async Task<Result<StyleArtPresetResponseDto>> CreateAsync(CreateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<StyleArtPresetResponseDto>(validation.ToValidationError());
        }

        if (GetAuthenticatedUserId() is not Guid userId)
            return Result.Failure<StyleArtPresetResponseDto>(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in to create art styles."));

        var presetId = Guid.NewGuid();
        var previewUrl = await images.UploadImageAsync(
            request.Preview!,
            StyleArtPresetRules.StorageKey(presetId),
            cancellationToken);

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

        await presets.AddAsync(preset, saveChange: true, cancellationToken);
        return Result.Success(Map(preset, isMine: true));
    }

    public async Task<Result<StyleArtPresetResponseDto>> UpdateAsync(Guid id, UpdateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<StyleArtPresetResponseDto>(validation.ToValidationError());
        }

        if (GetAuthenticatedUserId() is not Guid userId)
            return Result.Failure<StyleArtPresetResponseDto>(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in to update art styles."));

        var preset = await presets.GetByIdAsync(id, cancellationToken);
        if (preset is null)
            return Result.Failure<StyleArtPresetResponseDto>(Error.NotFound("StyleArtPresets.NotFound", "The art style was not found."));
        if (preset.UserId is null || preset.UserId != userId)
            return Result.Failure<StyleArtPresetResponseDto>(Error.Forbidden("StyleArtPresets.NotOwner", "Only the creator can update this art style."));

        preset.Name = request.Name.Trim();
        preset.Description = request.Description.Trim();
        preset.StyleModifiers = request.StyleModifiers.Trim();
        preset.Recommendations = SerializeRecommendations(request.RecommendationsJson);

        if (request.Preview is not null)
        {
            preset.PreviewImageUrl = await images.UploadImageAsync(
                request.Preview,
                StyleArtPresetRules.StorageKey(preset.Id),
                cancellationToken);
        }
        else if (request.DeletePreview && preset.PreviewImageUrl is not null)
        {
            await images.DeleteImageAsync(StyleArtPresetRules.StorageKey(preset.Id), cancellationToken);
            preset.PreviewImageUrl = null;
        }

        preset.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await presets.UpdateAsync(preset, saveChange: true, cancellationToken);
        return Result.Success(Map(preset, isMine: true));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (GetAuthenticatedUserId() is not Guid userId)
            return Result.Failure(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in to delete art styles."));

        var preset = await presets.GetByIdAsync(id, cancellationToken);
        if (preset is null)
            return Result.Failure(Error.NotFound("StyleArtPresets.NotFound", "The art style was not found."));

        if (preset.UserId is null || preset.UserId != userId)
            return Result.Failure(Error.Forbidden("StyleArtPresets.NotOwner", "System art styles cannot be deleted. Only the creator can delete this art style."));

        if (preset.PreviewImageUrl is not null)
        {
            await images.DeleteImageAsync(StyleArtPresetRules.StorageKey(preset.Id), cancellationToken);
        }

        await presets.RemoveAsync(preset, saveChange: true, cancellationToken);
        return Result.Success();
    }

    private Guid? GetAuthenticatedUserId() =>
        currentUser.IsAuthenticated && currentUser.UserId is Guid userId ? userId : null;

    private static StyleArtPresetResponseDto Map(StyleArtPreset preset, bool isMine) =>
        Map(preset.Id, preset.Name, preset.Description, preset.StyleModifiers, preset.PreviewImageUrl, preset.Recommendations, preset.IsSystemTemplate, isMine);

    private static StyleArtPresetResponseDto Map(
        Guid id,
        string name,
        string description,
        string styleModifiers,
        string? previewImageUrl,
        string recommendations,
        bool isSystemTemplate,
        bool isMine) =>
        new(id, name, description, styleModifiers, previewImageUrl, ToRecommendations(recommendations), isSystemTemplate, isMine);

    private static IReadOnlyList<string> ToRecommendations(string source)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(source) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeRecommendations(string? source)
    {
        if (!StyleArtPresetRules.TryParseRecommendations(source, out var recommendations))
        {
            return "[]";
        }

        return JsonSerializer.Serialize(recommendations);
    }
}
