using APCS.Api.Controllers;
using APCS.Application.Features.Admin;
using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AdminDashboardControllerTests
{
    private readonly Mock<IAdminDashboardService> _serviceMock = new();
    private readonly AdminDashboardController _controller;

    public AdminDashboardControllerTests()
    {
        _controller = new AdminDashboardController(_serviceMock.Object);
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenSuccessful_ReturnsOk()
    {
        var metrics = new AdminDashboardMetricsDto(
            100, 50, 5000m, 2000m, 10, 5,
            new List<MonthlyRevenueDto>(),
            new List<SupportTicketDto>(),
            new List<BatchJobDto>()
        );
        _serviceMock.Setup(s => s.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(metrics));

        var result = await _controller.GetMetricsAsync(CancellationToken.None);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeEquivalentTo(metrics);
    }

    [TestMethod]
    public async Task GetMetricsAsync_WhenFails_ReturnsProblemDetails()
    {
        var error = new Error("Code", "Message", ErrorType.Failure);
        _serviceMock.Setup(s => s.GetMetricsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AdminDashboardMetricsDto>(error));

        var result = await _controller.GetMetricsAsync(CancellationToken.None);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(500); // Because it uses ToActionResult under the hood which returns 500 for Failure type
        
        var problemDetails = objectResult.Value as ValidationProblemDetails ?? objectResult.Value as ProblemDetails;
        problemDetails.Should().NotBeNull();
    }
}
