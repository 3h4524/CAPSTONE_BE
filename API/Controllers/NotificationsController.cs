using APCS.Application.Features.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System;
using System.Threading.Tasks;

namespace APCS.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyNotifications()
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized();

        var notifications = await _notificationService.GetUserNotificationsAsync(userId);
        return Ok(notifications);
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized();

        var result = await _notificationService.MarkAsReadAsync(id, userId);
        if (!result) return NotFound();

        return Ok(new { success = true });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized();

        await _notificationService.MarkAllAsReadAsync(userId);
        return Ok(new { success = true });
    }
}
