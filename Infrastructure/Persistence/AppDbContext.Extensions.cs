using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Keeps application persistence contracts and custom mappings outside generated code.
/// </summary>
public partial class AppDbContext : IUnitOfWork, IReadDbContext
{
    public IQueryable<TEntity> Query<TEntity>()
        where TEntity : class =>
        Set<TEntity>().AsNoTracking();

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        return new AppDbTransaction(transaction);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthToken>()
            .Property<uint>("xmin")
            .IsRowVersion()
            .HasColumnName("xmin");
    }

    private sealed class AppDbTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
