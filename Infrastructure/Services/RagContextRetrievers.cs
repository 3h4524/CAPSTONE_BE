using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;

namespace APCS.Infrastructure.Services;

public sealed class ListingContextRetriever(IRagService ragService) : IListingContextRetriever
{
    public Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default) =>
        ragService.SearchAsync(new RagSearchRequest(query, topK, ContentType: RagContentTypes.Listing), cancellationToken);
}

public sealed class ImageContextRetriever(IRagService ragService) : IImageContextRetriever
{
    public Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default) =>
        ragService.SearchAsync(new RagSearchRequest(query, topK, ContentType: RagContentTypes.Image), cancellationToken);
}

public sealed class VideoContextRetriever(IRagService ragService) : IVideoContextRetriever
{
    public Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default) =>
        ragService.SearchAsync(new RagSearchRequest(query, topK, ContentType: RagContentTypes.Video), cancellationToken);
}

public sealed class PolicyContextRetriever(IRagService ragService) : IPolicyContextRetriever
{
    public Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default) =>
        ragService.SearchAsync(new RagSearchRequest(query, topK, ContentType: RagContentTypes.Policy), cancellationToken);
}
