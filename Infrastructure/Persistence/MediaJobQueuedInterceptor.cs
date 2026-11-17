using System.Runtime.CompilerServices;
using APCS.Application.Features.Workflows;
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

    // What the save in progress queues, kept as the object the weak table holds.
    private const int QueuesMedia = 1, MakesClips = 2;

    private void Inspect(DbContext? context)
    {
        if (context is null) return;
        var queues = 0;
        foreach (var entry in context.ChangeTracker.Entries<MediaJob>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Entity.Status == "queued" && entry.Entity.Kind is "render" or "export_zip") queues |= QueuesMedia;
            // The API has AI scenes to make for it: wakes the background pass that makes them.
            if (entry.Entity.Status == AiClipPhase.JobStatus && entry.Entity.Kind == "render") queues |= MakesClips;
        }
        if (queues != 0) saving.AddOrUpdate(context, queues);
    }

    private void Saved(DbContext? context)
    {
        if (context is null || !saving.TryGetValue(context, out var queued)) return;
        saving.Remove(context);
        var queues = (int)queued;
        if ((queues & QueuesMedia) != 0) afterCommit.Defer(context, activity.Media.Notify);
        if ((queues & MakesClips) != 0) afterCommit.Defer(context, activity.ClipPhase.Notify);
    }

    private void Forget(DbContext? context)
    {
        if (context is not null) saving.Remove(context);
    }
}
