using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace APCS.Api.Hubs;

[Authorize]
public class SupportHub : Hub
{
    private readonly IRepository<SupportTicket> _ticketRepository;

    public SupportHub(IRepository<SupportTicket> ticketRepository)
    {
        _ticketRepository = ticketRepository;
    }


    // Tham gia vào phòng chat của một Ticket cụ thể
    public async Task JoinTicket(string ticketId)
    {
        // Admin được phép join tất cả các phòng
        if (Context.User?.IsInRole("Admin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ticketId);
            return;
        }

        // Seller chỉ được join phòng của chính họ tạo ra
        if (Guid.TryParse(ticketId, out var parsedId))
        {
            var ticket = await _ticketRepository.GetByIdAsync(parsedId);
            if (ticket != null)
            {
                var userIdString = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                                   ?? Context.User?.FindFirst("sub")?.Value;
                                   
                if (Guid.TryParse(userIdString, out var userId) && ticket.UserId == userId)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, ticketId);
                }
            }
        }
    }

    // Rời khỏi phòng chat
    public async Task LeaveTicket(string ticketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ticketId);
    }
}
