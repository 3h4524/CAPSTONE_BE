using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.UnitTests.Persistence.Repositories;

[TestClass]
public sealed class SupportTicketRepositoryTests
{
    [TestMethod]
    public async Task ListAsync_ForOwner_ReturnsOnlyTheirTicketsInUpdatedOrder()
    {
        await using var context = CreateContext();
        var seller = User("seller@example.com");
        var other = User("other@example.com");
        context.Users.AddRange(seller, other);
        var older = Ticket(seller, "APCS-20260910-AAAAAA", DateTime.UtcNow.AddHours(-2));
        var newer = Ticket(seller, "APCS-20260910-BBBBBB", DateTime.UtcNow.AddHours(-1));
        context.SupportTickets.AddRange(older, newer, Ticket(other, "APCS-20260910-CCCCCC", DateTime.UtcNow));
        await context.SaveChangesAsync();
        var repository = new SupportTicketRepository(context);

        var result = await repository.ListAsync(seller.Id, null, null, null, 1, 20);

        result.TotalCount.Should().Be(2);
        result.Items.Select(ticket => ticket.Id).Should().Equal(newer.Id, older.Id);
    }

    [TestMethod]
    public async Task GetDetailsAsync_ForDifferentOwner_ReturnsNull()
    {
        await using var context = CreateContext();
        var owner = User("seller@example.com");
        var other = User("other@example.com");
        context.Users.AddRange(owner, other);
        var ticket = Ticket(owner, "APCS-20260910-AAAAAA", DateTime.UtcNow);
        context.SupportTickets.Add(ticket);
        await context.SaveChangesAsync();
        var repository = new SupportTicketRepository(context);

        var result = await repository.GetDetailsAsync(ticket.Id, other.Id, false);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetDetailsAsync_ForSeller_RemovesInternalNotesButKeepsPublicReplies()
    {
        await using var context = CreateContext();
        var seller = User("seller@example.com");
        var admin = User("admin@example.com");
        context.Users.AddRange(seller, admin);
        var ticket = Ticket(seller, "APCS-20260910-AAAAAA", DateTime.UtcNow);
        context.SupportTickets.Add(ticket);
        context.TicketReplies.AddRange(
            Reply(ticket, admin, "Visible answer", false),
            Reply(ticket, admin, "Internal investigation", true));
        await context.SaveChangesAsync();
        var repository = new SupportTicketRepository(context);

        var result = await repository.GetDetailsAsync(ticket.Id, seller.Id, false);

        result.Should().NotBeNull();
        result!.TicketReplies.Should().ContainSingle()
            .Which.ReplyText.Should().Be("Visible answer");
    }

    [TestMethod]
    public async Task GetDetailsAsync_ForAdmin_IncludesInternalNotes()
    {
        await using var context = CreateContext();
        var seller = User("seller@example.com");
        var admin = User("admin@example.com");
        context.Users.AddRange(seller, admin);
        var ticket = Ticket(seller, "APCS-20260910-AAAAAA", DateTime.UtcNow);
        context.SupportTickets.Add(ticket);
        context.TicketReplies.AddRange(
            Reply(ticket, admin, "Visible answer", false),
            Reply(ticket, admin, "Internal investigation", true));
        await context.SaveChangesAsync();
        var repository = new SupportTicketRepository(context);

        var result = await repository.GetDetailsAsync(ticket.Id, null, true);

        result.Should().NotBeNull();
        result!.TicketReplies.Should().HaveCount(2);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static User User(string email) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = email.Split('@')[0],
            AccountStatus = "active",
            CreatedAt = DateTime.UtcNow
        };

    private static SupportTicket Ticket(User user, string number, DateTime updatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            TicketNumber = number,
            Subject = "Publishing fails",
            Description = "Permissions error",
            Category = "integration",
            Priority = "normal",
            Status = "open",
            CreatedAt = updatedAt.AddMinutes(-1),
            UpdatedAt = updatedAt
        };

    private static TicketReply Reply(SupportTicket ticket, User author, string text, bool internalNote) =>
        new()
        {
            Id = Guid.NewGuid(),
            SupportTicketId = ticket.Id,
            SupportTicket = ticket,
            AuthorId = author.Id,
            Author = author,
            ReplyText = text,
            IsInternalNote = internalNote,
            CreatedAt = DateTime.UtcNow
        };
}
