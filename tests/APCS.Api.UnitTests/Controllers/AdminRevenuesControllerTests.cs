using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using APCS.Api.Controllers;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AdminRevenuesControllerTests
{
    private readonly Mock<IRevenueService> _serviceMock = new();
    private readonly AdminRevenuesController _controller;

    public AdminRevenuesControllerTests()
    {
        _controller = new AdminRevenuesController(_serviceMock.Object);
    }

    [TestMethod]
    public async Task GetMetrics_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var filter = new RevenueFilterDto();
        var metrics = new RevenueMetricsDto();
        _serviceMock.Setup(s => s.GetMetricsAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(metrics));

        // Act
        var result = await _controller.GetMetrics(filter, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeSameAs(metrics);
    }

    [TestMethod]
    public async Task GetMetrics_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var filter = new RevenueFilterDto();
        var error = new Error("Revenue.Failed", "Cannot calculate revenue metrics", ErrorType.Failure);
        _serviceMock.Setup(s => s.GetMetricsAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RevenueMetricsDto>(error));

        // Act
        var result = await _controller.GetMetrics(filter, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.Value.Should().Be(error);
    }

    [TestMethod]
    public async Task GetCharts_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var filter = new RevenueFilterDto();
        var charts = new RevenueChartDto();
        _serviceMock.Setup(s => s.GetChartsAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(charts));

        // Act
        var result = await _controller.GetCharts(filter, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeSameAs(charts);
    }

    [TestMethod]
    public async Task GetCharts_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var filter = new RevenueFilterDto();
        var error = new Error("Revenue.ChartsFailed", "Cannot calculate charts", ErrorType.Failure);
        _serviceMock.Setup(s => s.GetChartsAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RevenueChartDto>(error));

        // Act
        var result = await _controller.GetCharts(filter, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.Value.Should().Be(error);
    }

    [TestMethod]
    public async Task GetInvoices_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var filter = new RevenueFilterDto();
        var pagedInvoices = new PagedResult<RevenueInvoiceDto>(new List<RevenueInvoiceDto>(), 0, 1, 10);
        _serviceMock.Setup(s => s.GetInvoicesAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(pagedInvoices));

        // Act
        var result = await _controller.GetInvoices(filter, CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeSameAs(pagedInvoices);
    }

    [TestMethod]
    public async Task GetInvoices_WhenFailure_ReturnsBadRequest()
    {
        // Arrange
        var filter = new RevenueFilterDto();
        var error = new Error("Revenue.InvoicesFailed", "Cannot load invoices", ErrorType.Failure);
        _serviceMock.Setup(s => s.GetInvoicesAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PagedResult<RevenueInvoiceDto>>(error));

        // Act
        var result = await _controller.GetInvoices(filter, CancellationToken.None);

        // Assert
        var badRequestResult = result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.Value.Should().Be(error);
    }
}
