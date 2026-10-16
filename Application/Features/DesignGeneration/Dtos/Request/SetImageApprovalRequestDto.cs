namespace APCS.Application.Features.DesignGeneration.Dtos.Request;

/// <summary>Approves or rejects generated design images of a finished batch job (SRS 3.5.12).</summary>
/// <param name="DesignImageIds">
/// The images to change, at most 500. When omitted, every image of the job that is still <c>pending</c> is changed,
/// so "approve all" never overrides an image the Seller already rejected.
/// </param>
/// <param name="Status"><c>approved</c>, <c>rejected</c>, or <c>pending</c> to take a decision back.</param>
public sealed record SetImageApprovalRequestDto(IReadOnlyList<Guid>? DesignImageIds, string Status);
