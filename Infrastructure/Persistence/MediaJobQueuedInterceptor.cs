using System.Runtime.CompilerServices;
using APCS.Domain.Entities;
using APCS.Infrastructure.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Raises <see cref="WorkflowActivitySignal.Media"/> once a render or export job saved as queued is committed, so the
/// idle media dispatcher and failure sweeper wake when the job is already visible to them.
/// </summary>
public sealed class MediaJobQueuedInterceptor(WorkflowActivitySignal activity, AfterCommitInterceptor afterCommit)
    : ISaveChangesInterceptor
{
    // Contexts whose save in progress queues a job. Weak, so a disposed context is not kept.
    private readonly ConditionalWeakTable<DbContext, object> saving = new();

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Inspect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Inspect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Inspect(DbContext? context)
    {
        if (context is null) return;
        var queues = context.ChangeTracker.Entries<MediaJob>().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified
            && entry.Entity.Status == "queued" && entry.Entity.Kind is "render" or "export_zip");
        if (queues) saving.AddOrUpdate(context, true);
    }

    private void Saved(DbContext? context)
    {
        if (context is not null && saving.Remove(context))
            afterCommit.Defer(context, activity.Media.Notify);
    }

    private void Forget(DbContext? context)
    {
        if (context is not null) saving.Remove(context);
    }
}
