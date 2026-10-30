using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Abstractions.Rag;

public interface IVideoContextRetriever
{
    Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(string query, VideoExperienceSearchRequest scope,
        int topK = 5, CancellationToken cancellationToken = default);
}
