namespace APCS.Application.Features.DesignGeneration.Dtos.Request;

/// <summary>Sets products of a batch back to pending so the next job generates designs for them again.</summary>
/// <param name="ProductIds">
/// The products to reset, at most 500; each must be resettable. When omitted, every resettable product of the
/// batch is reset.
/// </param>
public sealed record ResetProductsRequestDto(IReadOnlyList<Guid>? ProductIds = null);
