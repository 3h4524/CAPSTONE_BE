using APCS.Application.Features.ListingContent.Dtos;
using APCS.Common.Models;

namespace APCS.Application.Features.ListingContent;

public interface IListingContentService
{
    Task<Result<ListingContentResponseDto>> GenerateAsync(
        Guid productId,
        CancellationToken cancellationToken = default,
        ListingSeoFeedbackDto? seoFeedback = null);

    Task<Result<ListingRetrievalEvaluationResponse>> EvaluateRetrievalForAdminAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The product's listing for a workflow step: the one it has when it was written from the product as it is now (or the
    /// seller edited it), so running a workflow again neither writes nor counts it twice; else a new one.
    /// </summary>
    Task<Result<ListingContentResponseDto>> GetOrGenerateAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<Result<ListingContentResponseDto>> GetAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<Result<ListingSeoScoreResponseDto>> EvaluateSeoAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
