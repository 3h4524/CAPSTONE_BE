using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.Notifications.Dtos.Request;
using APCS.Application.Features.Notifications.Dtos.Response;

namespace APCS.Application.Features.Notifications;

public interface INotificationService
{
    Task<NotificationDto> SendNotificationAsync(CreateNotificationDto request, CancellationToken cancellationToken = default);
    Task<IEnumerable<NotificationDto>> GetUserNotificationsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
