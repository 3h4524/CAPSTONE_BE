using APCS.Application.Features.SupportTickets.Dtos.Response;

namespace APCS.Application.Abstractions.Notifications;

public interface ISupportTicketNotifier
{
    Task NotifyTicketUpdatedAsync(Guid ticketId, Guid ticketOwnerId, SupportTicketReplyResponseDto? reply = null, CancellationToken cancellationToken = default);
}
