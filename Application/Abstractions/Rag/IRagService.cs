using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Abstractions.Rag;

public interface IRagService
{
    Task<Guid> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default);
}
