using System.Linq.Expressions;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides common persistence operations for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Finds an entity by its primary key.
    /// </summary>
    /// <param name="id">The primary key value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching entity, or <see langword="null"/> when none exists.</returns>
    Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every entity in the set.
    /// </summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds every entity matching the given predicate.
    /// </summary>
    /// <param name="predicate">The filter to apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an untracked query over the entity set.
    /// </summary>
    IQueryable<TEntity> Query();

    /// <summary>
    /// Adds an entity to the current unit of work.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="saveChange">
    /// When <see langword="true"/>, persists the change immediately instead of leaving it staged
    /// for the caller's own <c>IUnitOfWork.SaveChangesAsync</c> call.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task AddAsync(TEntity entity, bool saveChange = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an entity as modified on the current unit of work.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="saveChange">
    /// When <see langword="true"/>, persists the change immediately instead of leaving it staged
    /// for the caller's own <c>IUnitOfWork.SaveChangesAsync</c> call.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UpdateAsync(TEntity entity, bool saveChange = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an entity through the current unit of work.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <param name="saveChange">
    /// When <see langword="true"/>, persists the change immediately instead of leaving it staged
    /// for the caller's own <c>IUnitOfWork.SaveChangesAsync</c> call.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RemoveAsync(TEntity entity, bool saveChange = false, CancellationToken cancellationToken = default);
}
