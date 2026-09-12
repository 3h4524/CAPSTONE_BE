using APCS.Api.Controllers;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class SubscriptionsControllerTests
{
    [TestMethod]
    public void Controller_IsRestrictedToTheSellerRole()
    {
        var attribute = typeof(SubscriptionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        attribute.Should().NotBeNull();
        attribute!.Roles.Should().Be(AuthConstants.UserRole);
    }

    [TestMethod]
    public async Task GetOverview_WhenSuccessful_ReturnsOk()
    {
        var overview = new SubscriptionOverviewResponseDto(null, [], [], [], false);
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.GetOverviewAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(overview));
        var controller = CreateController(service);

        var action = await controller.GetOverview(CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(overview);
    }

    [TestMethod]
    public async Task GetOverview_WhenUnauthenticated_ReturnsProblem()
    {
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.GetOverviewAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SubscriptionOverviewResponseDto>(
                Error.Unauthorized(ErrorCodes.Unauthorized, "Not authenticated")));
        var controller = CreateController(service);

        var action = await controller.GetOverview(CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
    }

    [TestMethod]
    public async Task Checkout_WhenSuccessful_ReturnsOkAndForwardsTheRequest()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var response = new CheckoutResponseDto(Guid.NewGuid(), "raw-qr", "https://payos.vn/checkout/x", 2000);
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.InitiateCheckoutAsync(It.IsAny<CheckoutRequestDto>(), cancellationToken))
            .ReturnsAsync(Result.Success(response));
        var controller = CreateController(service);
        var request = new CheckoutRequestDto(Guid.NewGuid(), "monthly");

        var action = await controller.Checkout(request, cancellationToken);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(response);
        service.Verify(candidate => candidate.InitiateCheckoutAsync(request, cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task Checkout_WhenAlreadySubscribed_ReturnsProblem()
    {
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.InitiateCheckoutAsync(It.IsAny<CheckoutRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CheckoutResponseDto>(
                new Error(
                    ErrorCodes.SubscriptionAlreadySubscribed,
                    "You already have an active paid plan. Please use Upgrade or Downgrade instead.",
                    ErrorType.Validation)));
        var controller = CreateController(service);

        var action = await controller.Checkout(new CheckoutRequestDto(Guid.NewGuid(), "monthly"), CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
    }

    [TestMethod]
    public async Task GetCheckoutStatus_WhenSuccessful_ReturnsOk()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var invoiceId = Guid.NewGuid();
        var status = new CheckoutStatusResponseDto("pending", "Starter", "INV-1", null);
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.GetCheckoutStatusAsync(invoiceId, cancellationToken))
            .ReturnsAsync(Result.Success(status));
        var controller = CreateController(service);

        var action = await controller.GetCheckoutStatus(invoiceId, cancellationToken);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(status);
    }

    [TestMethod]
    public async Task GetCheckoutStatus_WhenInvoiceNotFound_ReturnsProblem()
    {
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.GetCheckoutStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CheckoutStatusResponseDto>(
                Error.NotFound(ErrorCodes.SubscriptionInvoiceNotFound, "Not found")));
        var controller = CreateController(service);

        var action = await controller.GetCheckoutStatus(Guid.NewGuid(), CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task CancelCheckout_WhenSuccessful_ReturnsNoContent()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var invoiceId = Guid.NewGuid();
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.CancelCheckoutAsync(invoiceId, cancellationToken))
            .ReturnsAsync(Result.Success());
        var controller = CreateController(service);

        var action = await controller.CancelCheckout(invoiceId, cancellationToken);

        action.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task CancelCheckout_WhenInvoiceNotFound_ReturnsProblem()
    {
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.CancelCheckoutAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.NotFound(ErrorCodes.SubscriptionInvoiceNotFound, "Not found")));
        var controller = CreateController(service);

        var action = await controller.CancelCheckout(Guid.NewGuid(), CancellationToken.None);

        action.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    private static SubscriptionsController CreateController(Mock<ISubscriptionService> service) =>
        new(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
