using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.StyleArtPresets;

public interface IStyleArtPresetService
{
    Task<Result<IReadOnlyList<StyleArtPresetResponseDto>>> ListActiveAsync(CancellationToken cancellationToken = default);
}
