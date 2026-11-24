using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>A new DI scope for each piece of work, acting as the given user (see <see cref="IOwnerScopes"/>).</summary>
public sealed class OwnerScopes(IServiceScopeFactory scopes) : IOwnerScopes
{
    public async Task<T> RunAsync<T>(Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ActingUserContext>().ActAs(userId);
        return await work(scope.ServiceProvider);
    }
}
