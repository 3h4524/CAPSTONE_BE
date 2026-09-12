using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists invoices with Entity Framework Core.
/// </summary>
public sealed class InvoiceRepository(AppDbContext dbContext)
    : Repository<Invoice>(dbContext), IInvoiceRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Invoice>> GetRecentWithPlanAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.Subscription)
            .ThenInclude(subscription => subscription.Plan)
            .Where(invoice => invoice.UserId == userId)
            // InvoiceDate alone (day precision only) ties whenever several invoices are created
            // on the same calendar day — a near-certainty once a Seller retries checkout more
            // than once — so CreatedAt (full timestamp) breaks the tie and keeps "recent" meaning
            // "most recently created", not an arbitrary same-day row (BR188).
            .OrderByDescending(invoice => invoice.InvoiceDate)
            .ThenByDescending(invoice => invoice.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Invoice?> GetByIdForUserAsync(
        Guid invoiceId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.Subscription)
            .ThenInclude(subscription => subscription.Plan)
            .Where(invoice => invoice.Id == invoiceId && invoice.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Invoice?> GetByPayosOrderCodeAsync(
        long orderCode,
        CancellationToken cancellationToken = default) =>
        dbContext.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.Subscription)
            .Where(invoice => invoice.PayosOrderCode == orderCode)
            .SingleOrDefaultAsync(cancellationToken);
}
