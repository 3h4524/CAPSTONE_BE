using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Email;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.SupportTickets;
using APCS.Application.Features.SupportTickets.Common;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using APCS.Application.Features.SupportTickets.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.SupportTickets;

[TestClass]
public sealed class SupportTicketServiceTests
{
    private static readonly Guid SellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GetMineAsync_WhenTicketBelongsToSomeoneElse_ReturnsNotFound()
    {
        var fixture = CreateFixture();
        fixture.Tickets.Setup(repository => repository.GetDetailsAsync(
                It.IsAny<Guid>(), SellerId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportTicket?)null);

        var result = await fixture.Service.GetMineAsync(Guid.NewGuid());

        result.Error.Code.Should().Be("support_tickets.not_found");
        fixture.Tickets.Verify(repository => repository.GetDetailsAsync(
            It.IsAny<Guid>(), SellerId, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ReplyAsync_WhenWaitingForCustomer_MovesTicketBackToInProgress()
    {
        var fixture = CreateFixture();
        var ticket = Ticket(SupportTicketStatuses.WaitingCustomer);
        fixture.Tickets.Setup(repository => repository.GetForUpdateAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await fixture.Service.ReplyAsync(
            ticket.Id,
            new CreateTicketReplyRequestDto("Here is the requested information.", []));

        result.IsSuccess.Should().BeTrue();
        result.Value.AuthorRole.Should().Be("Seller");
        ticket.Status.Should().Be(SupportTicketStatuses.InProgress);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow(SupportTicketStatuses.Resolved)]
    [DataRow(SupportTicketStatuses.Closed)]
    public async Task ReplyAsync_WhenTicketIsTerminal_ReturnsConflict(string status)
    {
        var fixture = CreateFixture();
        var ticket = Ticket(status);
        fixture.Tickets.Setup(repository => repository.GetForUpdateAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await fixture.Service.ReplyAsync(
            ticket.Id,
            new CreateTicketReplyRequestDto("Can you check again?", []));

        result.Error.Code.Should().Be("support_tickets.reply_not_allowed");
        fixture.Replies.Verify(repository => repository.AddAsync(
            It.IsAny<TicketReply>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RateAsync_WhenResolvedAndUnrated_PersistsRating()
    {
        var fixture = CreateFixture();
        var ticket = Ticket(SupportTicketStatuses.Resolved);
        fixture.Tickets.Setup(repository => repository.GetForUpdateAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await fixture.Service.RateAsync(ticket.Id, new RateSupportTicketRequestDto(5));

        result.IsSuccess.Should().BeTrue();
        result.Value.SatisfactionRating.Should().Be(5);
        ticket.SatisfactionRating.Should().Be(5);
    }

    [TestMethod]
    public async Task RateAsync_WhenAlreadyRated_DoesNotOverwriteRating()
    {
        var fixture = CreateFixture();
        var ticket = Ticket(SupportTicketStatuses.Resolved);
        ticket.SatisfactionRating = 4;
        fixture.Tickets.Setup(repository => repository.GetForUpdateAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await fixture.Service.RateAsync(ticket.Id, new RateSupportTicketRequestDto(1));

        result.Error.Code.Should().Be("support_tickets.rating_not_allowed");
        ticket.SatisfactionRating.Should().Be(4);
    }

    [TestMethod]
    public async Task UpdateAdminAsync_WithInvalidStatusJump_ReturnsConflict()
    {
        var fixture = CreateFixture();
        var ticket = Ticket(SupportTicketStatuses.Open);
        fixture.Tickets.Setup(repository => repository.GetForUpdateAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await fixture.Service.UpdateAdminAsync(
            ticket.Id,
            new UpdateSupportTicketRequestDto(Status: SupportTicketStatuses.Resolved));

        result.Error.Code.Should().Be("support_tickets.invalid_status_transition");
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_WhenDatabaseWorkFails_DeletesUploadedAssets()
    {
        var fixture = CreateFixture();
        fixture.Storage.Setup(storage => storage.UploadAsync(
                It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadFileDto _, string key, CancellationToken _) => new StoredFileDto(key, "stable-url"));
        fixture.Tickets.Setup(repository => repository.AddAsync(
                It.IsAny<SupportTicket>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database failed"));
        var request = new CreateSupportTicketRequestDto(
            "Publishing fails",
            "integration",
            "normal",
            "The publishing operation returns a permissions error.",
            [new UploadFileDto("error.log", "text/plain", 20, new MemoryStream(new byte[20]))]);

        var action = () => fixture.Service.CreateAsync(request);

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Storage.Verify(storage => storage.DeleteAsync(
            It.Is<string>(key => key.StartsWith("support-tickets/")), CancellationToken.None), Times.Once);
        fixture.Transaction.Verify(transaction => transaction.RollbackAsync(CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_WhenAdminEmailFails_KeepsTicketAndMarksDeliveryFailed()
    {
        var fixture = CreateFixture();
        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            FullName = "Admin",
            AccountStatus = "active"
        };
        var deliveries = new List<NotificationDelivery>();
        fixture.Accounts.Setup(repository => repository.GetActiveUsersByRoleAsync(
                "Admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([admin]);
        fixture.Deliveries.Setup(repository => repository.AddAsync(
                It.IsAny<NotificationDelivery>(), false, It.IsAny<CancellationToken>()))
            .Callback<NotificationDelivery, bool, CancellationToken>((delivery, _, _) => deliveries.Add(delivery))
            .Returns(Task.CompletedTask);
        fixture.Email.Setup(service => service.SendSupportTicketCreatedAsync(
                admin.Email,
                admin.FullName,
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                "integration",
                "normal",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp unavailable"));
        var request = new CreateSupportTicketRequestDto(
            "Publishing fails",
            "integration",
            "normal",
            "The publishing operation returns a permissions error.",
            []);

        var result = await fixture.Service.CreateAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SupportTicketStatuses.Open);
        deliveries.Should().HaveCount(2);
        deliveries.Single(delivery => delivery.Channel == "in_app").DeliveryStatus.Should().Be("sent");
        var emailDelivery = deliveries.Single(delivery => delivery.Channel == "email");
        emailDelivery.DeliveryStatus.Should().Be("failed");
        emailDelivery.RetryCount.Should().Be(1);
        fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    [DataRow(SupportTicketStatuses.Open, SupportTicketStatuses.InProgress)]
    [DataRow(SupportTicketStatuses.InProgress, SupportTicketStatuses.WaitingCustomer)]
    [DataRow(SupportTicketStatuses.WaitingCustomer, SupportTicketStatuses.Resolved)]
    [DataRow(SupportTicketStatuses.Resolved, SupportTicketStatuses.Closed)]
    public async Task UpdateAdminAsync_WithAllowedTransition_PersistsNewStatus(
        string currentStatus,
        string requestedStatus)
    {
        var fixture = CreateFixture();
        var ticket = Ticket(currentStatus);
        fixture.Tickets.Setup(repository => repository.GetForUpdateAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);
        fixture.Tickets.Setup(repository => repository.GetDetailsAsync(
                ticket.Id, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        var result = await fixture.Service.UpdateAdminAsync(
            ticket.Id,
            new UpdateSupportTicketRequestDto(Status: requestedStatus));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(requestedStatus);
        ticket.Status.Should().Be(requestedStatus);
        if (requestedStatus == SupportTicketStatuses.Resolved)
        {
            ticket.ResolvedAt.Should().Be(Now.UtcDateTime);
        }
    }

    private static Fixture CreateFixture()
    {
        var tickets = new Mock<ISupportTicketRepository>();
        var replies = new Mock<IRepository<TicketReply>>();
        var attachments = new Mock<IRepository<TicketAttachment>>();
        var alerts = new Mock<IRepository<NotificationAlert>>();
        var deliveries = new Mock<IRepository<NotificationDelivery>>();
        var accounts = new Mock<IAccountRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var transaction = new Mock<IUnitOfWorkTransaction>();
        var storage = new Mock<IFileStorageService>();
        var email = new Mock<IEmailService>();
        var currentUser = new Mock<ICurrentUser>();

        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(SellerId);
        tickets.Setup(repository => repository.TicketNumberExistsAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        accounts.Setup(repository => repository.GetActiveUsersByRoleAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        unitOfWork.Setup(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction.Object);
        unitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        transaction.Setup(item => item.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(item => item.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(item => item.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var service = new SupportTicketService(
            tickets.Object,
            replies.Object,
            attachments.Object,
            alerts.Object,
            deliveries.Object,
            accounts.Object,
            unitOfWork.Object,
            storage.Object,
            email.Object,
            currentUser.Object,
            new FakeTimeProvider(Now),
            new CreateSupportTicketValidator(),
            new ListSupportTicketsValidator(),
            new CreateTicketReplyValidator(),
            new CreateAdminTicketReplyValidator(),
            new UpdateSupportTicketValidator(),
            new RateSupportTicketValidator(),
            NullLogger<SupportTicketService>.Instance);

        return new Fixture(
            service,
            tickets,
            replies,
            accounts,
            deliveries,
            unitOfWork,
            transaction,
            storage,
            email);
    }

    private static SupportTicket Ticket(string status) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = SellerId,
            User = new User { Id = SellerId, FullName = "Seller", Email = "seller@example.com" },
            TicketNumber = "APCS-20260910-ABC123",
            Subject = "Publishing fails",
            Description = "The publishing operation returns a permissions error.",
            Category = "integration",
            Priority = "normal",
            Status = status,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        };

    private sealed record Fixture(
        SupportTicketService Service,
        Mock<ISupportTicketRepository> Tickets,
        Mock<IRepository<TicketReply>> Replies,
        Mock<IAccountRepository> Accounts,
        Mock<IRepository<NotificationDelivery>> Deliveries,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<IUnitOfWorkTransaction> Transaction,
        Mock<IFileStorageService> Storage,
        Mock<IEmailService> Email);
}
