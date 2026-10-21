using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using APCS.Api.Controllers;
using APCS.Application.Features.SystemStatistics;
using APCS.Application.Features.SystemStatistics.Dtos;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AdminStatisticsControllerTests
{
    private readonly Mock<ISystemStatisticsService> _serviceMock = new();
    private readonly AdminStatisticsController _controller;

    public AdminStatisticsControllerTests()
    {
        _controller = new AdminStatisticsController(_serviceMock.Object);
    }

    [TestMethod]
    public async Task GetOverview_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var filter = new StatisticsFilterDto { TimeRange = "30d" };
        var overview = new SystemStatisticsOverviewDto();
        _serviceMock.Setup(s => s.GetOverviewAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(overview));

        // Act
        var result = await _controller.GetOverview(filter, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeSameAs(overview);
    }

    [TestMethod]
    public async Task GetOverview_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var filter = new StatisticsFilterDto();
        var error = new Error("Statistics.Failed", "Failed to retrieve statistics", ErrorType.Failure);
        _serviceMock.Setup(s => s.GetOverviewAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SystemStatisticsOverviewDto>(error));

        // Act
        var result = await _controller.GetOverview(filter, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.Value.Should().Be(error);
    }

    [TestMethod]
    public async Task Export_WhenSuccess_ReturnsFileResult()
    {
        // Arrange
        var filter = new StatisticsFilterDto { TimeRange = "30d" };
        var bytes = Encoding.UTF8.GetBytes("col1,col2\nval1,val2");
        _serviceMock.Setup(s => s.ExportCsvAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(bytes));

        // Act
        var result = await _controller.Export(filter, CancellationToken.None);

        // Assert
        var fileResult = result as FileContentResult;
        fileResult.Should().NotBeNull();
        fileResult!.ContentType.Should().Contain("text/csv");
        fileResult.FileDownloadName.Should().StartWith("system_statistics_30d_");
        fileResult.FileContents.Should().BeSameAs(bytes);
    }

    [TestMethod]
    public async Task Export_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var filter = new StatisticsFilterDto();
        var error = new Error("Statistics.ExportFailed", "Failed to export statistics", ErrorType.Failure);
        _serviceMock.Setup(s => s.ExportCsvAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<byte[]>(error));

        // Act
        var result = await _controller.Export(filter, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.Value.Should().Be(error);
    }
}
