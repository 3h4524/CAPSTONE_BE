using System;

namespace APCS.Application.Features.Notifications.Dtos.Request;

public class CreateNotificationDto
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string Severity { get; set; } = "Info"; // Info, Warning, Error
    public string? ActionUrl { get; set; }
}
