using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Admin.StyleArtPresets;

public interface IAdminStyleArtPresetService
{
    Task<Result<IReadOnlyList<AdminStyleArtPresetDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<AdminStyleArtPresetDto>> CreateAsync(CreateAdminStyleArtPresetRequestDto request, CancellationToken cancellationToken = default);

    Task<Result<AdminStyleArtPresetDto>> UpdateAsync(Guid id, UpdateAdminStyleArtPresetRequestDto request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>A system art style as an admin edits it, with the machine translation and its status when present.</summary>
public sealed record AdminStyleArtPresetDto(
    Guid Id,
    string Name,
    string Description,
    string StyleModifiers,
    string? PreviewImageUrl,
    IReadOnlyList<string> Recommendations,
    bool IsActive,
    int UsageCount,
    AdminStyleArtPresetViDto? Vi,
    string? ViStatus);

/// <summary>The Vietnamese machine translation of a system style, read-only for the admin.</summary>
public sealed record AdminStyleArtPresetViDto(
    string Name,
    string Description,
    IReadOnlyList<string> Recommendations);

/// <summary>English content only; the Vietnamese translation is produced in the background.</summary>
public sealed record CreateAdminStyleArtPresetRequestDto(
    string Name,
    string Description,
    string StyleModifiers,
    string? RecommendationsJson,
    UploadFileDto? Preview);

/// <summary>Same as create, plus the active flag and preview deletion.</summary>
public sealed record UpdateAdminStyleArtPresetRequestDto(
    string Name,
    string Description,
    string StyleModifiers,
    string? RecommendationsJson,
    UploadFileDto? Preview,
    bool DeletePreview,
    bool IsActive);

public sealed class AdminStyleArtPresetService(
    ICurrentUser currentUser,
    IRepository<StyleArtPreset> presets,
    IPublicImageService images,
    ICacheService cache,
    IStyleArtPresetTranslationQueue translationQueue,
    TimeProvider timeProvider) : IAdminStyleArtPresetService
{
    public async Task<Result<IReadOnlyList<AdminStyleArtPresetDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await presets.Query()
            .Where(preset => preset.UserId == null)
            .OrderBy(preset => preset.Name)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<AdminStyleArtPresetDto>>(rows.Select(Map).ToList());
    }

    public async Task<Result<AdminStyleArtPresetDto>> CreateAsync(CreateAdminStyleArtPresetRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid _)
            return Result.Failure<AdminStyleArtPresetDto>(Error.Unauthorized("Auth.Unauthenticated", "Sign in again to manage styles."));

        if (Problem(request.Name, request.Description, request.StyleModifiers, request.RecommendationsJson) is { } problem)
            return Result.Failure<AdminStyleArtPresetDto>(Error.Validation(problem));

        if (await IsNameTakenAsync(request.Name, null, cancellationToken))
            return Result.Failure<AdminStyleArtPresetDto>(StyleArtPresetErrors.DuplicateName());

        var presetId = Guid.NewGuid();
        var storageKey = StorageKey(presetId);
        var previewUrl = request.Preview is null
            ? null
            : await images.UploadImageAsync(request.Preview, storageKey, cancellationToken);

        var name = request.Name.Trim();
        var description = request.Description?.Trim() ?? string.Empty;
        var modifiers = request.StyleModifiers?.Trim() ?? string.Empty;
        StyleArtPresetRules.TryParseRecommendations(request.RecommendationsJson, out var recommendations);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var preset = new StyleArtPreset
        {
            Id = presetId,
            UserId = null,
            Name = name,
            Description = description,
            StyleModifiers = modifiers,
            PreviewImageUrl = previewUrl,
            Recommendations = StyleArtPresetTranslations.SerializeRecommendations(recommendations),
            IsSystemTemplate = true,
            IsActive = true,
            UsageCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            Translations = StyleArtPresetTranslations.Queued(now)
        };

        try
        {
            await presets.AddAsync(preset, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (previewUrl is not null)
                await TryDeleteImageAsync(storageKey, cancellationToken);

            if (await IsNameTakenAsync(request.Name, null, cancellationToken))
                return Result.Failure<AdminStyleArtPresetDto>(StyleArtPresetErrors.DuplicateName());

            throw;
        }
        catch
        {
            if (previewUrl is not null)
                await TryDeleteImageAsync(storageKey, cancellationToken);
            throw;
        }

        await InvalidateSystemRowsAsync(cancellationToken);
        translationQueue.Notify();
        return Result.Success(Map(preset));
    }

    public async Task<Result<AdminStyleArtPresetDto>> UpdateAsync(Guid id, UpdateAdminStyleArtPresetRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid _)
            return Result.Failure<AdminStyleArtPresetDto>(Error.Unauthorized("Auth.Unauthenticated", "Sign in again to manage styles."));

        if (Problem(request.Name, request.Description, request.StyleModifiers, request.RecommendationsJson) is { } problem)
            return Result.Failure<AdminStyleArtPresetDto>(Error.Validation(problem));

        var preset = await presets.Query().SingleOrDefaultAsync(item => item.Id == id && item.UserId == null, cancellationToken);
        if (preset is null)
            return Result.Failure<AdminStyleArtPresetDto>(NotFound());

        if (await IsNameTakenAsync(request.Name, id, cancellationToken))
            return Result.Failure<AdminStyleArtPresetDto>(StyleArtPresetErrors.DuplicateName());

        var name = request.Name.Trim();
        var description = request.Description?.Trim() ?? string.Empty;
        var modifiers = request.StyleModifiers?.Trim() ?? string.Empty;
        StyleArtPresetRules.TryParseRecommendations(request.RecommendationsJson, out var recommendations);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var needsTranslation = NeedsTranslation(preset, name, description, recommendations);
        if (needsTranslation)
            preset.Translations = StyleArtPresetTranslations.Queued(now);
        preset.Name = name;
        preset.Description = description;
        preset.StyleModifiers = modifiers;
        preset.Recommendations = StyleArtPresetTranslations.SerializeRecommendations(recommendations);
        preset.IsActive = request.IsActive;
        preset.UpdatedAt = now;

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

        try
        {
            await presets.UpdateAsync(preset, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsNameTakenAsync(request.Name, id, cancellationToken))
                return Result.Failure<AdminStyleArtPresetDto>(StyleArtPresetErrors.DuplicateName());

            throw;
        }

        await InvalidateSystemRowsAsync(cancellationToken);
        if (needsTranslation)
            translationQueue.Notify();
        return Result.Success(Map(preset));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var preset = await presets.Query().SingleOrDefaultAsync(item => item.Id == id && item.UserId == null, cancellationToken);
        if (preset is null || !preset.IsActive)
            return Result.Failure(NotFound());

        preset.IsActive = false;
        preset.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await presets.UpdateAsync(preset, saveChange: true, cancellationToken);

        await InvalidateSystemRowsAsync(cancellationToken);
        return Result.Success();
    }

    private static bool NeedsTranslation(
        StyleArtPreset preset,
        string name,
        string description,
        IReadOnlyList<string> recommendations)
    {
        var state = StyleArtPresetTranslations.ParseState(preset.Translations);
        return state is null
            || state.Status == StyleArtPresetTranslations.Failed
            || preset.Name != name
            || preset.Description != description
            || !ToRecommendations(preset.Recommendations).SequenceEqual(recommendations);
    }

    private async Task InvalidateSystemRowsAsync(CancellationToken cancellationToken) =>
        await cache.RemoveAsync(StyleArtPresetCacheKeys.SystemRows, cancellationToken);

    private async Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var candidate = name.Trim().ToLowerInvariant();
        return await presets.Query()
            .AnyAsync(preset => preset.Name.ToLower() == candidate && preset.Id != excludeId, cancellationToken);
    }

    private async Task TryDeleteImageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await images.DeleteImageAsync(storageKey, cancellationToken);
        }
        catch (Exception)
        {
        }
    }

    private static string? Problem(
        string name,
        string? description,
        string? styleModifiers,
        string? recommendationsJson)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > StyleArtPresetRules.MaximumNameLength)
            return $"A style name of at most {StyleArtPresetRules.MaximumNameLength} characters is required.";
        if (description?.Trim().Length > StyleArtPresetRules.MaximumDescriptionLength)
            return $"The description must be at most {StyleArtPresetRules.MaximumDescriptionLength} characters.";
        if (styleModifiers?.Trim().Length > StyleArtPresetRules.MaximumModifiersLength)
            return $"The style modifiers must be at most {StyleArtPresetRules.MaximumModifiersLength} characters.";
        if (!StyleArtPresetRules.TryParseRecommendations(recommendationsJson, out _))
            return $"Recommendations must be a JSON array of up to {StyleArtPresetRules.MaximumRecommendations} short strings.";

        return null;
    }

    private static string StorageKey(Guid presetId) => $"style-art-presets/{presetId:N}";

    private static Error NotFound() => Error.NotFound("StyleArtPreset.NotFound", "System style preset not found.");

    private static IReadOnlyList<string> ToRecommendations(string source) =>
        StyleArtPresetRules.TryParseRecommendations(source, out var recommendations) ? recommendations : [];

    private static AdminStyleArtPresetViDto? ParseVi(string? translations)
    {
        var vi = StyleArtPresetTranslations.ParseViDocument(translations);
        return vi is null
            ? null
            : new AdminStyleArtPresetViDto(vi.Name, vi.Description, vi.Recommendations);
    }

    private static string? ViStatus(string? translations) =>
        StyleArtPresetTranslations.ParseState(translations)?.Status;

    private static AdminStyleArtPresetDto Map(StyleArtPreset preset) =>
        new(
            preset.Id,
            preset.Name,
            preset.Description,
            preset.StyleModifiers,
            preset.PreviewImageUrl,
            ToRecommendations(preset.Recommendations),
            preset.IsActive,
            preset.UsageCount,
            ParseVi(preset.Translations),
            ViStatus(preset.Translations));
}
