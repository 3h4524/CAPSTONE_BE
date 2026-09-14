using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

public interface IApiKeyRepository : IRepository<ApiKey>
{
    /// <summary>Reads display metadata without loading encrypted credentials or tracking entities.</summary>
    Task<IReadOnlyList<ApiKeyMetadata>> ListMetadataOwnedAsync(Guid userId, CancellationToken cancellationToken = default);

    // Serializes credential changes per owner across application instances.
    Task<IUnitOfWorkTransaction> LockOwnerAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiKey>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken);
}
