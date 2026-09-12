using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides persistence operations for invoices.
/// </summary>
public interface IInvoiceRepository : IRepository<Invoice>
{
    /// <summary>
    /// Finds a user's most recent invoices, newest first, with each invoice's plan loaded (BR188).
    /// </summary>
    Task<IReadOnlyList<Invoice>> GetRecentWithPlanAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds one invoice by id, scoped to its owner, with its subscription and plan loaded.
    /// </summary>
    Task<Invoice?> GetByIdForUserAsync(
        Guid invoiceId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the invoice a PayOS webhook's order code refers to, with its subscription loaded.
    /// </summary>
    Task<Invoice?> GetByPayosOrderCodeAsync(
        long orderCode,
        CancellationToken cancellationToken = default);
}
