using APCS.Application.Features.Workflows;

namespace APCS.Application.Abstractions.AI;

/// <summary>Prepares the same approved first-frame pixels for prompt analysis and MiniMax submission.</summary>
public interface IVideoFirstFramePreparer
{
    Task<byte[]> PrepareAsync(string sourceImageUrl, string aspectRatio, Region? sourceCrop,
        CancellationToken cancellationToken);
}
