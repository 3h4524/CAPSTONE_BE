using System.Reflection;
using System.Text;
using APCS.Api.Contracts.SupportTickets;
using APCS.Api.Controllers;
using APCS.Application.Features.SupportTickets;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using APCS.Application.Features.SupportTickets.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AdminSupportTicketsControllerTests
{
    [TestMethod]
    public async Task List_ReturnsOkResult()
    {
        var service = new Mock<ISupportTicketService>();
        var controller = new AdminSupportTicketsController(service.Object);
        var request = new ListSupportTicketsRequestDto(1, 10, null, null, null);
        var pagedResult = new PagedResult<SupportTicketSummaryResponseDto>(new List<SupportTicketSummaryResponseDto>(), 0, 1, 10);

        service.Setup(s => s.ListAdminAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(pagedResult));

        var result = await controller.List(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [TestMethod]
    public async Task Get_ReturnsOkResult()
    {
        var service = new Mock<ISupportTicketService>();
        var controller = new AdminSupportTicketsController(service.Object);
        var id = Guid.NewGuid();
        var dto = new SupportTicketDetailResponseDto(id, "TICKET-1", "Subject", "Description", "Category", "Priority", "open", null, Guid.NewGuid(), "User", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, new List<SupportTicketAttachmentResponseDto>(), new List<SupportTicketReplyResponseDto>());

        service.Setup(s => s.GetAdminAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(dto));

        var result = await controller.Get(id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [TestMethod]
    public async Task Update_ReturnsOkResult()
    {
        var service = new Mock<ISupportTicketService>();
        var controller = new AdminSupportTicketsController(service.Object);
        var id = Guid.NewGuid();
        var request = new UpdateSupportTicketRequestDto(Status: "resolved");
        var dto = new SupportTicketDetailResponseDto(id, "TICKET-1", "Subject", "Description", "Category", "Priority", "resolved", null, Guid.NewGuid(), "User", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new List<SupportTicketAttachmentResponseDto>(), new List<SupportTicketReplyResponseDto>());

        service.Setup(s => s.UpdateAdminAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(dto));

        var result = await controller.Update(id, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [TestMethod]
    public async Task Reply_MapsMultipartFieldsAndReturnsCreatedWithLocation()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var id = Guid.NewGuid();
        var service = new Mock<ISupportTicketService>();
        var replyDto = new SupportTicketReplyResponseDto(Guid.NewGuid(), id, "Admin", "Admin", "Admin Reply", false, DateTimeOffset.UtcNow, new List<APCS.Application.Features.SupportTickets.Dtos.Response.SupportTicketAttachmentResponseDto>());
        
        service.Setup(candidate => candidate.ReplyAdminAsync(
                id,
                It.IsAny<CreateAdminTicketReplyRequestDto>(), cancellationToken))
            .ReturnsAsync(Result.Success(replyDto));

        var controller = new AdminSupportTicketsController(service.Object);
        
        var bytes = Encoding.UTF8.GetBytes("log content");
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "attachments", "error.log")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var form = new CreateAdminTicketReplyForm
        {
            ReplyText = "Here is the fix",
            Attachments = [formFile]
        };

        var action = await controller.Reply(id, form, cancellationToken);

        var created = action.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(AdminSupportTicketsController.Get));
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(id);
        
        service.Verify(candidate => candidate.ReplyAdminAsync(
            id,
            It.Is<CreateAdminTicketReplyRequestDto>(req =>
                req.ReplyText == "Here is the fix" &&
                req.Attachments.Count == 1 &&
                req.Attachments.Single().FileName == "error.log"),
            cancellationToken), Times.Once);
    }
}
