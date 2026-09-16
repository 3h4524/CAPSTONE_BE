using APCS.Application.Features.ApiKeys.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.ApiKeys;

/// <summary>Provides the authenticated seller's credential overview and connection management.</summary>
public interface IApiKeyService
{
    Task<Result<IReadOnlyList<ApiKeyProviderResponseDto>>> ListProvidersAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveAsync(Guid? id, Dtos.Request.SaveApiKeyRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> ValidateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<ListApiKeysResponseDto>> ListMineAsync(CancellationToken cancellationToken = default);
}
