namespace APCS.Application.Features.DesignGeneration.Dtos.Response;

/// <summary>The result of queuing a batch job for background image generation.</summary>
public sealed record StartGenerationResponseDto(Guid BatchJobId, int QueuedProductCount, string Status);
