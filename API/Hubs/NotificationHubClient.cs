using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Notifications;
using APCS.Application.Features.Notifications.Dtos.Response;
using Microsoft.AspNetCore.SignalR;

namespace APCS.Api.Hubs;

public class NotificationHubClient : INotificationHubClient
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public NotificationHubClient(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendNotificationToUserAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.User(userId.ToString()).SendAsync("ReceiveNotification", notification, cancellationToken: cancellationToken);
    }

    public async Task SendNotificationToGroupAsync(string groupName, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group(groupName).SendAsync("ReceiveNotification", notification, cancellationToken: cancellationToken);
    }
}
