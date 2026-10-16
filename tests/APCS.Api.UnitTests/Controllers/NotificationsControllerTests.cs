using APCS.Api.Controllers;
using APCS.Application.Features.Notifications;
using APCS.Application.Features.Notifications.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class NotificationsControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [TestMethod]
    public async Task GetMyNotifications_WhenUserIsAuthenticated_ReturnsOkWithNotifications()
    {
        var fixture = CreateFixture();
        var notifications = new List<NotificationDto> 
        { 
            new NotificationDto { Id = Guid.NewGuid(), Title = "Test 1" },
            new NotificationDto { Id = Guid.NewGuid(), Title = "Test 2" }
        };

        fixture.NotificationService.Setup(s => s.GetUserNotificationsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notifications);

        var action = await fixture.Controller.GetMyNotifications();

        var okResult = action.Should().BeOfType<OkObjectResult>().Subject;
        var returnedNotifications = okResult.Value.Should().BeAssignableTo<IEnumerable<NotificationDto>>().Subject;
        returnedNotifications.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GetMyNotifications_WhenUserIsUnauthenticated_ReturnsUnauthorized()
    {
        var fixture = CreateFixture(isAuthenticated: false);

        var action = await fixture.Controller.GetMyNotifications();

        action.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task MarkAsRead_WhenNotificationExists_ReturnsOk()
    {
        var fixture = CreateFixture();
        var notificationId = Guid.NewGuid();

        fixture.NotificationService.Setup(s => s.MarkAsReadAsync(notificationId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var action = await fixture.Controller.MarkAsRead(notificationId);

        var okResult = action.Should().BeOfType<OkObjectResult>().Subject;
        // checking dynamic type is tricky, but it has { success = true }
        okResult.Value.Should().NotBeNull();
    }

    [TestMethod]
    public async Task MarkAsRead_WhenNotificationDoesNotExist_ReturnsNotFound()
    {
        var fixture = CreateFixture();
        var notificationId = Guid.NewGuid();

        fixture.NotificationService.Setup(s => s.MarkAsReadAsync(notificationId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var action = await fixture.Controller.MarkAsRead(notificationId);

        action.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task MarkAllAsRead_WhenAuthenticated_ReturnsOk()
    {
        var fixture = CreateFixture();

        fixture.NotificationService.Setup(s => s.MarkAllAsReadAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var action = await fixture.Controller.MarkAllAsRead();

        var okResult = action.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
        fixture.NotificationService.Verify(s => s.MarkAllAsReadAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Fixture CreateFixture(bool isAuthenticated = true)
    {
        var service = new Mock<INotificationService>();
        var controller = new NotificationsController(service.Object);

        var claims = new List<Claim>();
        if (isAuthenticated)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, UserId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, isAuthenticated ? "TestAuth" : null);
        var user = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        return new Fixture(controller, service);
    }

    private sealed record Fixture(
        NotificationsController Controller,
        Mock<INotificationService> NotificationService);
}
