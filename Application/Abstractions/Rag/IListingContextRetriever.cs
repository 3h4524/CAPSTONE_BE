using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Abstractions.Rag;

public interface IListingContextRetriever
{
    Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default);
}
