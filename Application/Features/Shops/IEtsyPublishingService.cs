using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Shops;

/// <summary>Sends a product's approved content and media to the seller's Etsy shop as a draft listing.</summary>
public interface IEtsyPublishingService
{
    /// <summary>The push form for a product: content, approved media, the shop's choices and remembered settings.</summary>
    Task<Result<EtsyDraftResponseDto>> GetDraftAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Etsy categories whose path contains <paramref name="query"/>.</summary>
    Task<Result<IReadOnlyList<EtsyTaxonomyOptionDto>>> SearchTaxonomyAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the draft listing and uploads the chosen images and video. Pushing the same content again returns
    /// the draft already made rather than a second one. Nothing is published: the seller does that on Etsy.
    /// </summary>
    Task<Result<PublishEtsyDraftResponseDto>> PublishDraftAsync(Guid productId, PublishEtsyDraftRequestDto request, CancellationToken cancellationToken = default);
}
