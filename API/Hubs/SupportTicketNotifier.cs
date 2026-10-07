using APCS.Application.Abstractions.Notifications;
using Microsoft.AspNetCore.SignalR;

using APCS.Application.Features.SupportTickets.Dtos.Response;

namespace APCS.Api.Hubs;

public class SupportTicketNotifier : ISupportTicketNotifier
{
    private readonly IHubContext<SupportHub> _hubContext;

    public SupportTicketNotifier(IHubContext<SupportHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyTicketUpdatedAsync(Guid ticketId, Guid ticketOwnerId, SupportTicketReplyResponseDto? reply = null, CancellationToken cancellationToken = default)
    {
        // 1. Gửi tin nhắn vào phòng chat (Dành cho user đang mở sẵn khung chat)
        _ = _hubContext.Clients.Group(ticketId.ToString()).SendAsync("ReceiveNewMessage", reply, cancellationToken: cancellationToken);

        await Task.CompletedTask;
    }
}
