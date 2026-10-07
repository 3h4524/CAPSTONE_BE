using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.Notifications.Dtos.Response;

namespace APCS.Application.Abstractions.Notifications;

public interface INotificationHubClient
{
    Task SendNotificationToUserAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default);
    Task SendNotificationToGroupAsync(string groupName, NotificationDto notification, CancellationToken cancellationToken = default);
}
