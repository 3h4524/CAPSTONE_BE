namespace APCS.Application.Common.Interfaces;

/// <summary>
/// Represents an application database transaction.
/// </summary>
public interface IAppDbTransaction : IAsyncDisposable
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
