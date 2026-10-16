using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Notifications;
using APCS.Application.Features.Notifications.Dtos.Request;
using APCS.Application.Features.Notifications.Dtos.Response;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Notifications;

public class NotificationService : INotificationService
{
    private readonly IRepository<NotificationAlert> _notificationRepository;
    private readonly INotificationHubClient _hubClient;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationService(
        IRepository<NotificationAlert> notificationRepository,
        INotificationHubClient hubClient,
        IUnitOfWork unitOfWork)
    {
        _notificationRepository = notificationRepository;
        _hubClient = hubClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationDto> SendNotificationAsync(CreateNotificationDto request, CancellationToken cancellationToken = default)
    {
        var existingList = await _notificationRepository.FindAsync(n => 
            n.UserId == request.UserId && 
            n.IsRead != true && 
            n.ActionUrl != null && 
            n.ActionUrl == request.ActionUrl && 
            n.Type == request.Type, cancellationToken);
            
        var existing = existingList.FirstOrDefault();

        if (existing != null)
        {
            existing.CreatedAt = DateTime.UtcNow;
            
            var title = existing.Title;
            int count = 2;
            
            var match = System.Text.RegularExpressions.Regex.Match(title, @"\((\d+) mới\)$");
            if (match.Success)
            {
                count = int.Parse(match.Groups[1].Value) + 1;
                title = title.Substring(0, match.Index).TrimEnd();
            }
            
            existing.Title = $"{title} ({count} mới)";
            existing.Message = request.Message;
            
            await _notificationRepository.UpdateAsync(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var updatedDto = new NotificationDto
            {
                Id = existing.Id,
                UserId = existing.UserId,
                Type = existing.Type,
                Title = existing.Title,
                Message = existing.Message,
                Severity = existing.Severity,
                IsRead = existing.IsRead ?? false,
                ActionUrl = existing.ActionUrl,
                CreatedAt = existing.CreatedAt ?? DateTime.UtcNow
            };

            await _hubClient.SendNotificationToUserAsync(request.UserId, updatedDto, cancellationToken);
            return updatedDto;
        }

        var notification = new NotificationAlert
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = request.Type,
            Title = request.Title,
            Message = request.Message,
            Severity = request.Severity,
            IsRead = false,
            ActionUrl = request.ActionUrl,
            CreatedAt = DateTime.UtcNow
        };

        await _notificationRepository.AddAsync(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = new NotificationDto
        {
            Id = notification.Id,
            UserId = notification.UserId,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            Severity = notification.Severity,
            IsRead = notification.IsRead ?? false,
            ActionUrl = notification.ActionUrl,
            CreatedAt = notification.CreatedAt ?? DateTime.UtcNow
        };

        // Bắn SignalR Real-time
        await _hubClient.SendNotificationToUserAsync(request.UserId, dto, cancellationToken);

        return dto;
    }

    public async Task<IEnumerable<NotificationDto>> GetUserNotificationsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Get all notifications for the user, ordered by newest first
        var notifications = await _notificationRepository.FindAsync(n => n.UserId == userId, cancellationToken);
        
        // Return top 50 for performance
        return notifications.OrderByDescending(n => n.CreatedAt).Take(50).Select(n => new NotificationDto
        {
            Id = n.Id,
            UserId = n.UserId,
            Type = n.Type,
            Title = n.Title,
            Message = n.Message,
            Severity = n.Severity,
            IsRead = n.IsRead ?? false,
            ActionUrl = n.ActionUrl,
            CreatedAt = n.CreatedAt ?? DateTime.UtcNow
        });
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification == null || notification.UserId != userId) return false;

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;

        await _notificationRepository.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationRepository.FindAsync(n => n.UserId == userId && n.IsRead != true, cancellationToken);
        
        foreach (var n in notifications)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            await _notificationRepository.UpdateAsync(n);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
