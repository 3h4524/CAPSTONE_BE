using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>Queries support-ticket aggregates with ownership enforced in SQL.</summary>
public sealed class SupportTicketRepository(AppDbContext dbContext)
    : Repository<SupportTicket>(dbContext), ISupportTicketRepository
{
    public async Task<(IReadOnlyList<SupportTicket> Items, int TotalCount)> ListAsync(
        Guid? ownerId,
        string? status,
        string? category,
        string? priority,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Query();
        if (ownerId.HasValue)
        {
            query = query.Where(ticket => ticket.UserId == ownerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(ticket => ticket.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(ticket => ticket.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            query = query.Where(ticket => ticket.Priority == priority);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(ticket => ticket.UpdatedAt)
            .ThenByDescending(ticket => ticket.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<SupportTicket?> GetDetailsAsync(
        Guid id,
        Guid? ownerId,
        bool includeInternalNotes,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.SupportTickets
            .AsNoTracking()
            .AsSplitQuery()
            .Include(ticket => ticket.User)
            .Include(ticket => ticket.AssignedToNavigation)
            .Include(ticket => ticket.TicketAttachments)
            .Include(ticket => ticket.TicketReplies.Where(reply => includeInternalNotes || reply.IsInternalNote != true))
                .ThenInclude(reply => reply.Author)
            .Include(ticket => ticket.TicketReplies.Where(reply => includeInternalNotes || reply.IsInternalNote != true))
                .ThenInclude(reply => reply.TicketAttachments)
            .Where(ticket => ticket.Id == id);

        if (ownerId.HasValue)
        {
            query = query.Where(ticket => ticket.UserId == ownerId.Value);
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public Task<SupportTicket?> GetForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.SupportTickets
            .Include(ticket => ticket.User)
            .Include(ticket => ticket.AssignedToNavigation)
            .SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);

    public Task<bool> TicketNumberExistsAsync(
        string ticketNumber,
        CancellationToken cancellationToken = default) =>
        Query().AnyAsync(ticket => ticket.TicketNumber == ticketNumber, cancellationToken);
}
