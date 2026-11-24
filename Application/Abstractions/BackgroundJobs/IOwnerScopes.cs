namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Runs a piece of background work in a fresh scope of its own, acting as the given user, so the pieces of one job can
/// run side by side: a scope's database context serves one piece of work at a time.
/// </summary>
public interface IOwnerScopes
{
    /// <summary>Runs <paramref name="work"/> with the services of a new scope that acts as <paramref name="userId"/>.</summary>
    Task<T> RunAsync<T>(Guid userId, Func<IServiceProvider, Task<T>> work);
}
