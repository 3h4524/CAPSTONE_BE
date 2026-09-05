namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Exposes read-only query roots for application queries.
/// </summary>
/// <remarks>
/// Queries returned here are untracked and always evaluated against the database. They therefore
/// do not observe writes that have been staged through <see cref="IUnitOfWork"/> but not yet
/// saved: a handler that adds an entity and reads it back before calling
/// <see cref="IUnitOfWork.SaveChangesAsync"/> will not find it.
/// </remarks>
public interface IReadDbContext
{
    /// <summary>
    /// Starts an untracked query over the given entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity to query.</typeparam>
    IQueryable<TEntity> Query<TEntity>()
        where TEntity : class;
}
