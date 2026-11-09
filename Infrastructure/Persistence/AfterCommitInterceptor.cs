using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Runs in-process side effects (a wake-up signal, an in-memory queue item) only once the database work they announce
/// is committed. Announced earlier, a background worker can look before the rows are visible, find nothing and drop
/// it; or announce work that is then rolled back.
/// </summary>
public sealed class AfterCommitInterceptor : IDbTransactionInterceptor
{
    // Actions waiting for each context's open transaction. Weak, so a disposed context is not kept.
    private readonly ConditionalWeakTable<DbContext, List<Action>> pending = new();

    /// <summary>Runs <paramref name="action"/> once the context's open transaction commits, or at once when none is open.</summary>
    public void Defer(DbContext context, Action action) =>
        Defer(context, action, context.Database.CurrentTransaction is not null);

    internal void Defer(DbContext context, Action action, bool inTransaction)
    {
        if (!inTransaction)
        {
            action();
            return;
        }
        lock (pending) pending.GetOrCreateValue(context).Add(action);
    }

    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        // A transaction disposed without a commit or an explicit rollback leaves its actions behind; drop them.
        Drop(eventData.Context);
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        Drop(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Committed(eventData.Context);

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Committed(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Drop(eventData.Context);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Drop(eventData.Context);
        return Task.CompletedTask;
    }

    internal void Committed(DbContext? context)
    {
        if (context is null) return;
        List<Action>? actions;
        lock (pending)
        {
            if (!pending.TryGetValue(context, out actions)) return;
            pending.Remove(context);
        }
        foreach (var action in actions) action();
    }

    internal void Drop(DbContext? context)
    {
        if (context is null) return;
        lock (pending) pending.Remove(context);
    }
}
