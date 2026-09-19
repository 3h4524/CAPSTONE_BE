using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.StyleArtPresets;

/// <summary>Manages system art styles and seller-created styles for the image-generation select step.</summary>
public interface IStyleArtPresetService
{
    /// <summary>Lists active system styles plus the current seller's own styles.</summary>
    Task<Result<IReadOnlyList<StyleArtPresetResponseDto>>> ListMineAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one visible style by id.</summary>
    Task<Result<StyleArtPresetResponseDto>> GetMineAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a seller-owned style with an uploaded preview image.</summary>
    Task<Result<StyleArtPresetResponseDto>> CreateAsync(CreateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Updates a seller-owned style.</summary>
    Task<Result<StyleArtPresetResponseDto>> UpdateAsync(Guid id, UpdateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a seller-owned style and its preview image.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
