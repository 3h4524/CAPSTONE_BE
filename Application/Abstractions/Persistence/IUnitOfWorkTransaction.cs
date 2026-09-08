namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Represents an application database transaction.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current transaction.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
