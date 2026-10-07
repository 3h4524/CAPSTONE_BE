using APCS.Application.Abstractions.Notifications;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Notifications;
using APCS.Application.Features.Notifications.Dtos.Request;
using APCS.Application.Features.Notifications.Dtos.Response;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;
using System.Linq.Expressions;

namespace APCS.Application.UnitTests.Features.Notifications;

[TestClass]
public sealed class NotificationServiceTests
{
    [TestMethod]
    public async Task SendNotificationAsync_WhenNoExistingNotification_CreatesNewNotification()
    {
        // Arrange
        var fixture = CreateFixture();
        
        // Setup repository to return empty list (no existing notification)
        fixture.NotificationRepository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<NotificationAlert, bool>>>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NotificationAlert>());

        var request = new CreateNotificationDto
        {
            UserId = Guid.NewGuid(),
            Type = "support_ticket_reply",
            Title = "Tin nhắn mới từ vé APCS-123",
            Message = "Khách hàng vừa gửi phản hồi mới.",
            Severity = "info",
            ActionUrl = "/admin/support-tickets?ticketId=123"
        };

        // Act
        var result = await fixture.Service.SendNotificationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("Tin nhắn mới từ vé APCS-123");
        
        // Verify AddAsync was called once
        fixture.NotificationRepository.Verify(r => r.AddAsync(It.IsAny<NotificationAlert>(), false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.NotificationRepository.Verify(r => r.UpdateAsync(It.IsAny<NotificationAlert>(), false, It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.HubClient.Verify(h => h.SendNotificationToUserAsync(request.UserId, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SendNotificationAsync_WhenExistingNotificationWithoutCount_AppendsCount()
    {
        // Arrange
        var fixture = CreateFixture();
        var existingAlert = new NotificationAlert
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = "support_ticket_reply",
            Title = "Tin nhắn mới từ vé APCS-123",
            Message = "Khách hàng vừa gửi phản hồi mới.",
            IsRead = false,
            ActionUrl = "/admin/support-tickets?ticketId=123",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        fixture.NotificationRepository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<NotificationAlert, bool>>>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NotificationAlert> { existingAlert });

        var request = new CreateNotificationDto
        {
            UserId = existingAlert.UserId,
            Type = "support_ticket_reply",
            Title = "Tin nhắn mới từ vé APCS-123",
            Message = "Nội dung mới nhất.",
            Severity = "info",
            ActionUrl = "/admin/support-tickets?ticketId=123"
        };

        // Act
        var result = await fixture.Service.SendNotificationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("Tin nhắn mới từ vé APCS-123 (2 mới)"); // Should append (2 mới)
        result.Message.Should().Be("Nội dung mới nhất.");
        
        fixture.NotificationRepository.Verify(r => r.AddAsync(It.IsAny<NotificationAlert>(), false, It.IsAny<CancellationToken>()), Times.Never);
        fixture.NotificationRepository.Verify(r => r.UpdateAsync(existingAlert, false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.HubClient.Verify(h => h.SendNotificationToUserAsync(request.UserId, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SendNotificationAsync_WhenExistingNotificationWithCount_IncrementsCount()
    {
        // Arrange
        var fixture = CreateFixture();
        var existingAlert = new NotificationAlert
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = "support_ticket_reply",
            Title = "Tin nhắn mới từ vé APCS-123 (4 mới)",
            Message = "Khách hàng vừa gửi phản hồi mới.",
            IsRead = false,
            ActionUrl = "/admin/support-tickets?ticketId=123",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        fixture.NotificationRepository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<NotificationAlert, bool>>>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NotificationAlert> { existingAlert });

        var request = new CreateNotificationDto
        {
            UserId = existingAlert.UserId,
            Type = "support_ticket_reply",
            Title = "Tin nhắn mới từ vé APCS-123", // Even if request title doesn't have count
            Message = "Tin nhắn thứ 5.",
            Severity = "info",
            ActionUrl = "/admin/support-tickets?ticketId=123"
        };

        // Act
        var result = await fixture.Service.SendNotificationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("Tin nhắn mới từ vé APCS-123 (5 mới)"); // 4 + 1
        result.Message.Should().Be("Tin nhắn thứ 5.");
        
        fixture.NotificationRepository.Verify(r => r.UpdateAsync(existingAlert, false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.HubClient.Verify(h => h.SendNotificationToUserAsync(request.UserId, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Fixture CreateFixture()
    {
        var notificationRepository = new Mock<IRepository<NotificationAlert>>();
        var hubClient = new Mock<INotificationHubClient>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var service = new NotificationService(
            notificationRepository.Object,
            hubClient.Object,
            unitOfWork.Object);

        return new Fixture(
            service,
            notificationRepository,
            hubClient,
            unitOfWork);
    }

    private sealed record Fixture(
        NotificationService Service,
        Mock<IRepository<NotificationAlert>> NotificationRepository,
        Mock<INotificationHubClient> HubClient,
        Mock<IUnitOfWork> UnitOfWork);
}
