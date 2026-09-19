using APCS.Api.Controllers;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class AdminSubscriptionPlansControllerTests
{
    private readonly Mock<IAdminSubscriptionPlanService> _serviceMock = new();
    private readonly AdminSubscriptionPlansController _controller;

    public AdminSubscriptionPlansControllerTests()
    {
        _controller = new AdminSubscriptionPlansController(_serviceMock.Object);
    }

    private static AdminPlanDto SamplePlan() => new(
        Guid.NewGuid(), "Creator", "creator", "For growing sellers",
        49m, 490m, 200, 2, 2000, 1000, 100, 20000, 200m,
        true, true, false, true, 1, 0, true, DateTime.UtcNow, DateTime.UtcNow);

    [TestMethod]
    public async Task GetAll_WhenSuccessful_ReturnsOk()
    {
        var plans = new List<AdminPlanDto> { SamplePlan() };
        _serviceMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<AdminPlanDto>>(plans));

        var result = await _controller.GetAll(CancellationToken.None);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeEquivalentTo(plans);
    }

    [TestMethod]
    public async Task GetById_WhenPlanNotFound_ReturnsNotFound()
    {
        var error = Error.NotFound("subscriptions.plan_not_found", "The subscription plan could not be found.");
        _serviceMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AdminPlanDto>(error));

        var result = await _controller.GetById(Guid.NewGuid(), CancellationToken.None);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task Create_WhenSuccessful_ReturnsOk()
    {
        var plan = SamplePlan();
        _serviceMock.Setup(s => s.CreateAsync(It.IsAny<CreatePlanRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(plan));

        var result = await _controller.Create(
            new CreatePlanRequestDto("Creator", "creator", null, 49m, 490m, 200, 2, 2000, 1000, 100, 20000, 200m, true, true, false, true, 1),
            CancellationToken.None);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeEquivalentTo(plan);
    }

    [TestMethod]
    public async Task Create_WhenValidationFails_ReturnsBadRequest()
    {
        var error = Error.Validation("One or more validation errors occurred.");
        _serviceMock.Setup(s => s.CreateAsync(It.IsAny<CreatePlanRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AdminPlanDto>(error));

        var result = await _controller.Create(
            new CreatePlanRequestDto("", "creator", null, 49m, 490m, 200, 2, 2000, 1000, 100, 20000, 200m, true, true, false, true, 1),
            CancellationToken.None);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(400);
    }

    [TestMethod]
    public async Task Update_WhenSuccessful_ReturnsOk()
    {
        var plan = SamplePlan();
        _serviceMock.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdatePlanRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(plan));

        var result = await _controller.Update(
            plan.Id,
            new UpdatePlanRequestDto("Creator", null, 49m, 490m, 200, 2, 2000, 1000, 100, 20000, 200m, true, true, false, true, 1),
            CancellationToken.None);

        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeEquivalentTo(plan);
    }

    [TestMethod]
    public async Task Delete_WhenSuccessful_ReturnsNoContent()
    {
        _serviceMock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<DeletePlanRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Delete(Guid.NewGuid(), new DeletePlanRequestDto("No longer offered"), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task Delete_WhenPlanHasSubscriptionHistory_ReturnsConflict()
    {
        var error = new Error(
            "subscriptions.plan_has_subscription_history",
            "This plan cannot be deleted because one or more Sellers have subscribed to it.",
            ErrorType.Conflict);
        _serviceMock.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<DeletePlanRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(error));

        var result = await _controller.Delete(Guid.NewGuid(), new DeletePlanRequestDto("Retiring this tier"), CancellationToken.None);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(409);
    }
}
