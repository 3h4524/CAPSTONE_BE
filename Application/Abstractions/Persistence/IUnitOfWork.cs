namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Coordinates persistence operations that belong to one application use case.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Begins a database transaction for operations that must be atomic.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all pending changes in the current unit of work.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
