using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// Provides lifecycle and projection-oriented persistence operations for support tickets.
/// </summary>
public interface ISupportTicketRepository : IRepository<SupportTicket>
{
    /// <summary>Lists tickets using server-side filtering and pagination.</summary>
    Task<(IReadOnlyList<SupportTicket> Items, int TotalCount)> ListAsync(
        Guid? ownerId,
        string? status,
        string? category,
        string? priority,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a complete untracked ticket graph for display.</summary>
    Task<SupportTicket?> GetDetailsAsync(
        Guid id,
        Guid? ownerId,
        bool includeInternalNotes,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a tracked ticket graph for lifecycle changes.</summary>
    Task<SupportTicket?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a generated public ticket number already exists.</summary>
    Task<bool> TicketNumberExistsAsync(string ticketNumber, CancellationToken cancellationToken = default);
}
