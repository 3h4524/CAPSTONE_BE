using System;

namespace APCS.Application.Features.Notifications.Dtos.Response;

public class NotificationDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public bool IsRead { get; set; }
    public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
