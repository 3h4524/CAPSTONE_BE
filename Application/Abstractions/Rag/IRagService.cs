using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Abstractions.Rag;

public interface IRagService
{
    Task<Guid> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default);
    Task ProcessIngestAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RagAdminDocumentDto>> GetAdminDocumentsAsync(string? status, string? sourceId, CancellationToken cancellationToken = default);
    Task<RagAdminDocumentDetailDto?> GetAdminDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<bool> ActivateDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
