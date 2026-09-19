using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.StyleArtPresets;

public interface IStyleArtPresetService
{
    Task<Result<IReadOnlyList<StyleArtPresetResponseDto>>> ListMineAsync(CancellationToken cancellationToken = default);

    Task<Result<StyleArtPresetResponseDto>> GetMineAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<StyleArtPresetResponseDto>> CreateAsync(CreateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default);

    Task<Result<StyleArtPresetResponseDto>> UpdateAsync(Guid id, UpdateStyleArtPresetRequestDto request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
