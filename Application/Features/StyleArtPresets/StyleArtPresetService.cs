using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.StyleArtPresets;

/// <summary>Lists the system-provided art styles for the image-generation select step.</summary>
public sealed class StyleArtPresetService(
    ICurrentUser currentUser,
    IRepository<StyleArtPreset> presets) : IStyleArtPresetService
{
    public async Task<Result<IReadOnlyList<StyleArtPresetResponseDto>>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure<IReadOnlyList<StyleArtPresetResponseDto>>(Error.Unauthorized("StyleArtPresets.Unauthenticated", "Please sign in to view art styles."));

        var rows = await presets.Query()
            .Where(preset => preset.IsActive)
            .OrderByDescending(preset => preset.UsageCount)
            .ThenBy(preset => preset.Name)
            .Select(preset => new
            {
                preset.Id,
                preset.Name,
                preset.Description,
                preset.StyleModifiers,
                preset.PreviewImageUrl,
                preset.Recommendations
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<StyleArtPresetResponseDto> items = rows
            .Select(row => new StyleArtPresetResponseDto(
                row.Id,
                row.Name,
                row.Description,
                row.StyleModifiers,
                row.PreviewImageUrl,
                ToRecommendations(row.Recommendations)))
            .ToList();

        return Result.Success(items);
    }

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
}
