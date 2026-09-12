using System.Reflection;
using System.Text;
using APCS.Api.Contracts.SupportTickets;
using APCS.Api.Controllers;
using APCS.Application.Features.SupportTickets;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using APCS.Application.Features.SupportTickets.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class SupportTicketsControllerTests
{
    [TestMethod]
    public async Task Create_MapsMultipartFieldsAndReturnsCreatedWithLocation()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var ticket = Summary();
        var service = new Mock<ISupportTicketService>();
        service.Setup(candidate => candidate.CreateAsync(
                It.IsAny<CreateSupportTicketRequestDto>(), cancellationToken))
            .ReturnsAsync(Result.Success(ticket));
        var controller = new SupportTicketsController(service.Object);
        var bytes = Encoding.UTF8.GetBytes("log content");
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "attachments", "error.log")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var action = await controller.Create(
            new CreateSupportTicketForm
            {
                Subject = "Publishing fails",
                Category = "integration",
                Priority = "normal",
                Description = "The provider returned a permissions error.",
                Attachments = [formFile]
            },
            cancellationToken);

        var created = action.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(SupportTicketsController.Get));
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(ticket.Id);
        service.Verify(candidate => candidate.CreateAsync(
            It.Is<CreateSupportTicketRequestDto>(request =>
                request.Subject == "Publishing fails" &&
                request.Priority == "normal" &&
                request.Attachments.Count == 1 &&
                request.Attachments.Single().FileName == "error.log"),
            cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task Get_WhenServiceHidesTicket_ReturnsNotFoundProblem()
    {
        var service = new Mock<ISupportTicketService>();
        service.Setup(candidate => candidate.GetMineAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SupportTicketDetailResponseDto>(
                Error.NotFound("support_tickets.not_found", "Not found")));
        var controller = new SupportTicketsController(service.Object);

        var action = await controller.Get(Guid.NewGuid(), CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public void Controllers_RequireExpectedRoles()
    {
        var seller = typeof(SupportTicketsController).GetCustomAttribute<AuthorizeAttribute>();
        var admin = typeof(AdminSupportTicketsController).GetCustomAttribute<AuthorizeAttribute>();

        seller.Should().NotBeNull();
        seller!.Roles.Should().Be(AuthConstants.UserRole);
        admin.Should().NotBeNull();
        admin!.Roles.Should().Be(AuthConstants.AdminRole);
    }

    private static SupportTicketSummaryResponseDto Summary() =>
        new(
            Guid.NewGuid(),
            "APCS-20260910-ABC123",
            "Publishing fails",
            "integration",
            "normal",
            "open",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
}
