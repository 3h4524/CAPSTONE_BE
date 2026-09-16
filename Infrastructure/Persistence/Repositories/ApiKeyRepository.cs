using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

public sealed class ApiKeyRepository : Repository<ApiKey>, IApiKeyRepository
{
    private readonly AppDbContext db;
    public ApiKeyRepository(AppDbContext db) : base(db) => this.db = db;

    public async Task<IReadOnlyList<ApiKeyMetadata>> ListMetadataOwnedAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        await Query()
            .Where(key => key.UserId == userId && key.DeletedAt == null)
            .OrderBy(key => key.ServiceProvider).ThenBy(key => key.KeyIdentifier).ThenBy(key => key.Id)
            .Select(key => new ApiKeyMetadata(
                key.Id, key.ServiceProvider, key.KeyIdentifier, key.KeyLast4, key.AuthType,
                key.ConnectedAccountName, key.Environment, key.IsActive, key.ExpiresAt,
                key.LastUsedAt, key.LastCheckedAt, key.LastCheckSucceeded))
            .ToListAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> LockOwnerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({userId.ToString()}, 0))", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ApiKey>> ListOwnedAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ApiKeys.Where(x => x.UserId == userId && x.DeletedAt == null).ToListAsync(cancellationToken);
}
