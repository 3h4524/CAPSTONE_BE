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

    Task<Result<ListingContentResponseDto>> GetAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<Result<ListingSeoScoreResponseDto>> EvaluateSeoAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
